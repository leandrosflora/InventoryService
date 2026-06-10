# InventoryService

Microserviço de inventário responsável por consultar disponibilidade, reservar estoque para checkouts, confirmar ou liberar reservas e registrar ajustes de saldo físico. A aplicação foi construída com ASP.NET Core Minimal APIs, Entity Framework Core e PostgreSQL, usando um padrão de Outbox para persistir eventos de domínio junto com as alterações transacionais.

## Sumário

- [Visão geral](#visão-geral)
- [Principais responsabilidades](#principais-responsabilidades)
- [Arquitetura e organização do projeto](#arquitetura-e-organização-do-projeto)
- [Tecnologias e dependências](#tecnologias-e-dependências)
- [Configuração](#configuração)
- [Banco de dados](#banco-de-dados)
- [Como executar localmente](#como-executar-localmente)
- [Documentação da API](#documentação-da-api)
- [Modelos de domínio](#modelos-de-domínio)
- [Fluxos de negócio](#fluxos-de-negócio)
- [Eventos gravados no Outbox](#eventos-gravados-no-outbox)
- [Health check e Swagger](#health-check-e-swagger)
- [Exemplos de requisições](#exemplos-de-requisições)
- [Observabilidade e resiliência](#observabilidade-e-resiliência)
- [Estratégia de testes sugerida](#estratégia-de-testes-sugerida)
- [Boas práticas operacionais](#boas-práticas-operacionais)

## Visão geral

O `InventoryService` centraliza o controle de estoque por vendedor (`sellerId`), SKU (`skuId`) e centro de atendimento/fulfillment (`fulfillmentCenterId`). O serviço mantém duas quantidades principais para cada item:

- `onHandQuantity`: quantidade física disponível no centro de atendimento.
- `reservedQuantity`: quantidade já bloqueada por reservas pendentes.

A disponibilidade retornada pela API é calculada como:

```text
availableQuantity = onHandQuantity - reservedQuantity
```

As operações críticas de reserva, confirmação, liberação, expiração e ajuste de estoque são executadas dentro de transações para preservar consistência entre inventário, reservas e mensagens do Outbox.

## Principais responsabilidades

- Consultar disponibilidade de estoque em lote por vendedor e lista de SKUs.
- Consultar disponibilidade de um SKU específico por vendedor.
- Criar reservas de estoque para um checkout com suporte a idempotência.
- Confirmar reservas, baixando estoque físico e removendo a quantidade reservada.
- Liberar reservas pendentes, devolvendo a quantidade reservada ao saldo disponível.
- Expirar reservas pendentes automaticamente após o prazo configurado no domínio.
- Ajustar estoque físico com motivo de ajuste.
- Registrar eventos no Outbox para integração assíncrona com outros serviços.
- Expor health check para validação da aplicação e do acesso ao banco.

## Arquitetura e organização do projeto

A solução segue uma separação simples por camadas:

```text
InventoryService/
├── Api/                         # Minimal APIs e mapeamento de endpoints HTTP
├── Application/                 # Casos de uso e orquestração transacional
│   └── Ports/                   # Interfaces para persistência, eventos e transações
├── Contracts/                   # DTOs de entrada e saída da API
├── Domain/                      # Entidades e regras de domínio
├── Infrastructure/
│   ├── Outbox/                  # Persistência de eventos no padrão Outbox
│   └── Persistence/             # DbContext, repositórios, worker e schema SQL
├── Properties/                  # Perfis de execução local
├── Program.cs                   # Configuração da aplicação e DI
├── InventoryService.csproj      # Dependências e target framework
└── InventoryService.http        # Exemplos de chamadas HTTP
```

### Camada `Api`

A camada `Api` define os endpoints HTTP em `/inventory`, delegando a execução das regras para os serviços de aplicação.

### Camada `Application`

A camada `Application` contém os casos de uso:

- `InventoryApplicationService`: consulta disponibilidade e ajusta estoque.
- `ReservationApplicationService`: cria, confirma e libera reservas.

Ela depende apenas das portas (`Application/Ports`) para acessar persistência, transações e publicação de eventos.

### Camada `Domain`

A camada `Domain` concentra as entidades e invariantes principais:

- `InventoryItem`
- `InventoryReservation`
- `ReservationItem`
- `ReservationStatus`
- `StockMovement`

### Camada `Infrastructure`

A camada `Infrastructure` implementa:

- Persistência com PostgreSQL via Entity Framework Core.
- Operações SQL atômicas para alteração de quantidades de estoque.
- Transações via `EfCoreTransactionRunner`.
- Gravação de eventos em `outbox_messages`.
- Worker de expiração automática de reservas.

## Tecnologias e dependências

- .NET 8 (`net8.0`).
- ASP.NET Core Minimal APIs.
- Entity Framework Core.
- PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`.
- Swagger/OpenAPI via `Swashbuckle.AspNetCore`.
- Health checks com `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`.

## Configuração

A configuração principal fica em `appsettings.json`.

```json
{
  "ConnectionStrings": {
    "InventoryDb": "Host=localhost;Port=5432;Database=inventory;Username=inventory;Password=inventory"
  }
}
```

A aplicação espera uma connection string chamada `InventoryDb`. Em ambientes produtivos, recomenda-se sobrescrever esse valor por variável de ambiente, secret manager ou configuração segura da plataforma:

```bash
ConnectionStrings__InventoryDb="Host=<host>;Port=5432;Database=<database>;Username=<user>;Password=<password>"
```

## Banco de dados

O serviço usa PostgreSQL. O script base de criação das tabelas está em `Infrastructure/Persistence/schema.sql`.

### Tabelas principais

#### `inventory_items`

Armazena o saldo por vendedor, SKU e centro de atendimento.

Campos principais:

- `id`
- `seller_id`
- `sku_id`
- `fulfillment_center_id`
- `on_hand_quantity`
- `reserved_quantity`
- `updated_at`

Restrições importantes:

- Chave única por `seller_id`, `sku_id` e `fulfillment_center_id`.
- Quantidades não negativas.
- `reserved_quantity` não pode ser maior que `on_hand_quantity`.

#### `inventory_reservations`

Armazena reservas criadas para checkouts.

Campos principais:

- `id`
- `checkout_id`
- `seller_id`
- `idempotency_key`
- `status`
- `created_at`
- `expires_at`
- `confirmed_at`
- `released_at`

Restrições importantes:

- `idempotency_key` única.
- Índice por `status` e `expires_at` para busca de reservas pendentes expiradas.

#### `inventory_reservation_items`

Armazena os itens de cada reserva.

Campos principais:

- `id`
- `reservation_id`
- `sku_id`
- `fulfillment_center_id`
- `quantity`

#### `outbox_messages`

Armazena eventos de domínio em JSON para processamento assíncrono por outro componente.

Campos principais:

- `id`
- `event_type`
- `payload`
- `created_at`
- `processed_at`

## Como executar localmente

### Pré-requisitos

- SDK do .NET 8 instalado.
- PostgreSQL disponível localmente ou em container.
- Banco `inventory` criado com usuário e senha compatíveis com a connection string.

### 1. Criar banco e aplicar schema

Exemplo usando `psql`:

```bash
createdb inventory
psql "Host=localhost Port=5432 Database=inventory User ID=inventory Password=inventory" -f Infrastructure/Persistence/schema.sql
```

> Ajuste host, porta, usuário e senha conforme o seu ambiente.

### 2. Restaurar dependências

```bash
dotnet restore
```

### 3. Compilar

```bash
dotnet build
```

### 4. Executar

```bash
dotnet run
```

Em desenvolvimento, os perfis locais expõem a API em:

- HTTP: `http://localhost:5168`
- HTTPS: `https://localhost:7183`

## Documentação da API

Todos os endpoints de negócio ficam sob o prefixo `/inventory`.

### `GET /health`

Verifica a saúde da aplicação e a conectividade com o `InventoryDbContext`.

#### Respostas

- `200 OK`: aplicação e dependências saudáveis.
- `503 Service Unavailable`: alguma dependência monitorada está indisponível.

---

### `POST /inventory/availability/batch`

Consulta a disponibilidade de uma lista de SKUs para um vendedor.

#### Request body

```json
{
  "sellerId": "11111111-1111-1111-1111-111111111111",
  "skuIds": [
    "22222222-2222-2222-2222-222222222222"
  ]
}
```

#### Response `200 OK`

```json
[
  {
    "sellerId": "11111111-1111-1111-1111-111111111111",
    "skuId": "22222222-2222-2222-2222-222222222222",
    "fulfillmentCenterId": "33333333-3333-3333-3333-333333333333",
    "onHandQuantity": 10,
    "reservedQuantity": 2,
    "availableQuantity": 8
  }
]
```

#### Regras

- `sellerId` é obrigatório e não pode ser `Guid.Empty`.
- Se `skuIds` estiver vazio, a resposta será uma lista vazia.
- SKUs duplicados são removidos antes da consulta.

---

### `GET /inventory/{sellerId}/{skuId}`

Consulta a disponibilidade de um SKU específico para um vendedor.

#### Exemplo

```http
GET /inventory/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222
```

#### Response `200 OK`

Retorna uma lista com os saldos encontrados para o par `sellerId` e `skuId`, incluindo os centros de atendimento correspondentes.

---

### `POST /inventory/reservations`

Cria uma reserva de estoque para um checkout.

#### Headers obrigatórios

```http
Idempotency-Key: checkout-44444444-4444-4444-4444-444444444444
```

O header `Idempotency-Key` é obrigatório. Se a mesma chave for enviada novamente, a API retorna a reserva já existente em vez de criar uma nova.

#### Request body

```json
{
  "checkoutId": "44444444-4444-4444-4444-444444444444",
  "sellerId": "11111111-1111-1111-1111-111111111111",
  "items": [
    {
      "skuId": "22222222-2222-2222-2222-222222222222",
      "fulfillmentCenterId": "33333333-3333-3333-3333-333333333333",
      "quantity": 1
    }
  ]
}
```

#### Response `201 Created`

```json
{
  "reservationId": "55555555-5555-5555-5555-555555555555",
  "checkoutId": "44444444-4444-4444-4444-444444444444",
  "sellerId": "11111111-1111-1111-1111-111111111111",
  "status": "Pending",
  "expiresAt": "2026-06-10T12:15:00Z",
  "items": [
    {
      "skuId": "22222222-2222-2222-2222-222222222222",
      "fulfillmentCenterId": "33333333-3333-3333-3333-333333333333",
      "quantity": 1
    }
  ]
}
```

#### Regras

- A reserva deve conter ao menos um item.
- Cada item deve ter `skuId`, `fulfillmentCenterId` e `quantity` maior que zero.
- A criação tenta reservar cada item de forma atômica.
- Se qualquer item não tiver estoque suficiente, os itens já reservados na mesma operação são liberados e a operação falha.
- Reservas criadas iniciam com status `Pending`.
- O prazo de expiração padrão é de 15 minutos após a criação.

---

### `POST /inventory/reservations/{reservationId}/confirm`

Confirma uma reserva pendente.

#### Exemplo

```http
POST /inventory/reservations/55555555-5555-5555-5555-555555555555/confirm
```

#### Response `200 OK`

```json
{
  "reservationId": "55555555-5555-5555-5555-555555555555",
  "checkoutId": "44444444-4444-4444-4444-444444444444",
  "sellerId": "11111111-1111-1111-1111-111111111111",
  "status": "Confirmed",
  "expiresAt": "2026-06-10T12:15:00Z",
  "items": [
    {
      "skuId": "22222222-2222-2222-2222-222222222222",
      "fulfillmentCenterId": "33333333-3333-3333-3333-333333333333",
      "quantity": 1
    }
  ]
}
```

#### Regras

- Apenas reservas `Pending` podem ser confirmadas.
- Reservas expiradas não podem ser confirmadas.
- Ao confirmar, o serviço reduz `reserved_quantity` e `on_hand_quantity` na quantidade reservada.

---

### `POST /inventory/reservations/{reservationId}/release`

Libera uma reserva pendente.

#### Exemplo

```http
POST /inventory/reservations/55555555-5555-5555-5555-555555555555/release
```

#### Response `200 OK`

```json
{
  "reservationId": "55555555-5555-5555-5555-555555555555",
  "checkoutId": "44444444-4444-4444-4444-444444444444",
  "sellerId": "11111111-1111-1111-1111-111111111111",
  "status": "Released",
  "expiresAt": "2026-06-10T12:15:00Z",
  "items": [
    {
      "skuId": "22222222-2222-2222-2222-222222222222",
      "fulfillmentCenterId": "33333333-3333-3333-3333-333333333333",
      "quantity": 1
    }
  ]
}
```

#### Regras

- Reservas pendentes podem ser liberadas.
- Reservas já liberadas ou expiradas não sofrem alteração adicional.
- Reservas confirmadas não podem ser liberadas diretamente.
- Ao liberar uma reserva pendente, o serviço reduz `reserved_quantity`, aumentando a disponibilidade.

---

### `POST /inventory/adjustments`

Ajusta o estoque físico de um SKU em um centro de atendimento.

#### Request body

```json
{
  "sellerId": "11111111-1111-1111-1111-111111111111",
  "skuId": "22222222-2222-2222-2222-222222222222",
  "fulfillmentCenterId": "33333333-3333-3333-3333-333333333333",
  "quantityDelta": 5,
  "reason": "Entrada de mercadoria"
}
```

#### Response `202 Accepted`

Sem corpo de resposta.

#### Regras

- `sellerId`, `skuId` e `fulfillmentCenterId` são obrigatórios.
- `quantityDelta` não pode ser zero.
- Ajustes negativos são permitidos somente quando o novo `on_hand_quantity` continuar maior ou igual ao `reserved_quantity`.
- O ajuste registra um evento `InventoryAdjusted` no Outbox.

## Modelos de domínio

### `InventoryItem`

Representa o saldo de um SKU em um centro de atendimento para um vendedor.

Invariantes:

- IDs de vendedor, SKU e fulfillment center são obrigatórios.
- `onHandQuantity` não pode ser negativo.
- `AvailableQuantity` é derivado de `OnHandQuantity - ReservedQuantity`.
- Ajustes não podem deixar o estoque físico menor que o reservado.

### `InventoryReservation`

Representa uma reserva de estoque vinculada a um checkout.

Campos importantes:

- `Status`: `Pending`, `Confirmed`, `Released` ou `Expired`.
- `CreatedAt`: data de criação.
- `ExpiresAt`: data limite para confirmação.
- `ConfirmedAt`: data de confirmação, quando aplicável.
- `ReleasedAt`: data de liberação ou expiração, quando aplicável.

Regras importantes:

- Toda reserva precisa de `checkoutId`, `sellerId`, `idempotencyKey` e ao menos um item.
- Apenas reservas pendentes podem ser confirmadas.
- Reservas expiradas não podem ser confirmadas.
- Reservas confirmadas não podem ser liberadas diretamente.

### `ReservationItem`

Representa um item reservado.

Invariantes:

- `skuId` obrigatório.
- `fulfillmentCenterId` obrigatório.
- `quantity` maior que zero.

## Fluxos de negócio

### Consulta de disponibilidade

1. Cliente informa `sellerId` e uma lista de `skuIds`.
2. Serviço remove SKUs duplicados.
3. Repositório consulta `inventory_items`.
4. API retorna quantidades física, reservada e disponível.

### Criação de reserva

1. Cliente envia `Idempotency-Key`, `checkoutId`, `sellerId` e itens.
2. Serviço verifica se já existe reserva com a mesma chave de idempotência.
3. Se existir, retorna a reserva existente.
4. Se não existir, tenta reservar item a item.
5. Para cada item, o banco executa update condicional garantindo saldo disponível suficiente.
6. Se algum item falhar, os itens já reservados nessa tentativa são liberados.
7. Reserva é persistida com status `Pending`.
8. Evento `InventoryReserved` é gravado no Outbox.

### Confirmação de reserva

1. Serviço carrega a reserva pelo ID.
2. Verifica se a reserva existe, está pendente e não expirou.
3. Muda status para `Confirmed`.
4. Para cada item, reduz `reserved_quantity` e `on_hand_quantity`.
5. Evento `InventoryReservationConfirmed` é gravado no Outbox.

### Liberação de reserva

1. Serviço carrega a reserva pelo ID.
2. Se estiver pendente, altera status para `Released`.
3. Para cada item, reduz `reserved_quantity`.
4. Evento `InventoryReservationReleased` é gravado no Outbox.

### Expiração automática de reservas

O `ReservationExpirationWorker` roda periodicamente a cada 30 segundos. Em cada execução:

1. Busca até 100 reservas pendentes com `expires_at` menor ou igual ao horário atual.
2. Marca cada reserva como `Expired`.
3. Libera as quantidades reservadas de cada item.
4. Grava evento `InventoryReservationExpired` no Outbox.
5. Salva as alterações dentro de uma transação.

## Eventos gravados no Outbox

O serviço grava eventos na tabela `outbox_messages`. Esses eventos ficam disponíveis para um publicador externo ou job de integração processar e marcar como enviados.

### `InventoryAdjusted`

Gerado quando o estoque físico é ajustado.

Payload contém:

- `SellerId`
- `SkuId`
- `FulfillmentCenterId`
- `QuantityDelta`
- `Reason`
- `OccurredAt`

### `InventoryReserved`

Gerado quando uma reserva é criada.

Payload contém:

- `Id`
- `CheckoutId`
- `SellerId`
- `ExpiresAt`
- `Items`

### `InventoryReservationConfirmed`

Gerado quando uma reserva é confirmada.

Payload contém:

- `Id`
- `CheckoutId`
- `SellerId`
- `ConfirmedAt`

### `InventoryReservationReleased`

Gerado quando uma reserva é liberada.

Payload contém:

- `Id`
- `CheckoutId`
- `SellerId`
- `ReleasedAt`

### `InventoryReservationExpired`

Gerado quando uma reserva pendente expira automaticamente.

Payload contém:

- `Id`
- `CheckoutId`
- `SellerId`
- `ExpiresAt`

## Health check e Swagger

- Health check: `GET /health`.
- Swagger UI em ambiente de desenvolvimento: `/swagger`.

O Swagger é habilitado apenas quando `ASPNETCORE_ENVIRONMENT=Development`.

## Exemplos de requisições

O arquivo `InventoryService.http` contém exemplos prontos para execução em IDEs compatíveis com arquivos `.http`, como Visual Studio, JetBrains Rider e extensões REST Client do Visual Studio Code.

### Criar uma reserva

```http
POST http://localhost:5168/inventory/reservations
Content-Type: application/json
Idempotency-Key: checkout-44444444-4444-4444-4444-444444444444

{
  "checkoutId": "44444444-4444-4444-4444-444444444444",
  "sellerId": "11111111-1111-1111-1111-111111111111",
  "items": [
    {
      "skuId": "22222222-2222-2222-2222-222222222222",
      "fulfillmentCenterId": "33333333-3333-3333-3333-333333333333",
      "quantity": 1
    }
  ]
}
```

### Confirmar uma reserva

```http
POST http://localhost:5168/inventory/reservations/55555555-5555-5555-5555-555555555555/confirm
```

### Liberar uma reserva

```http
POST http://localhost:5168/inventory/reservations/55555555-5555-5555-5555-555555555555/release
```

### Ajustar estoque

```http
POST http://localhost:5168/inventory/adjustments
Content-Type: application/json

{
  "sellerId": "11111111-1111-1111-1111-111111111111",
  "skuId": "22222222-2222-2222-2222-222222222222",
  "fulfillmentCenterId": "33333333-3333-3333-3333-333333333333",
  "quantityDelta": 10,
  "reason": "Carga inicial"
}
```

## Observabilidade e resiliência

- O serviço usa logs padrão do ASP.NET Core configurados em `appsettings.json`.
- O worker de expiração captura exceções, registra erro e continua executando nas próximas iterações.
- O health check valida o `InventoryDbContext`.
- O padrão Outbox reduz risco de inconsistência entre alterações no banco e publicação de eventos.

## Estratégia de testes sugerida

Embora este repositório não contenha projeto de testes automatizados, recomenda-se cobrir:

### Testes unitários

- Validações de `InventoryItem`.
- Validações de `ReservationItem`.
- Transições de status de `InventoryReservation`.
- Cálculo de disponibilidade.

### Testes de aplicação

- Criação de reserva com sucesso.
- Criação idempotente com mesma `Idempotency-Key`.
- Falha por estoque insuficiente com rollback dos itens já reservados.
- Confirmação de reserva pendente.
- Bloqueio de confirmação de reserva expirada.
- Liberação de reserva pendente.
- Bloqueio de liberação direta de reserva confirmada.
- Ajuste positivo e negativo de estoque.

### Testes de integração

- Execução dos endpoints HTTP com PostgreSQL real ou containerizado.
- Verificação das mensagens gravadas em `outbox_messages`.
- Execução do worker de expiração sobre reservas pendentes vencidas.

## Boas práticas operacionais

- Proteger a connection string fora do repositório em ambientes reais.
- Processar `outbox_messages` por um publicador dedicado e marcar `processed_at` após envio bem-sucedido.
- Monitorar crescimento da tabela de Outbox e criar estratégia de retenção.
- Monitorar reservas pendentes próximas do vencimento e falhas no worker de expiração.
- Usar IDs e chaves de idempotência estáveis no consumidor para evitar reservas duplicadas.
- Garantir que ajustes manuais de estoque sempre tenham `reason` descritivo.
- Avaliar criação de migrations do EF Core caso o schema evolua com frequência.

## Limitações conhecidas e pontos de evolução

- Não há autenticação/autorização configurada nos endpoints.
- Não há publicador de Outbox implementado neste repositório; o serviço apenas persiste as mensagens.
- Não há projeto de testes automatizados incluído.
- A criação de novos registros em `inventory_items` não é exposta por endpoint dedicado; o ajuste assume que o item já existe.
- Erros de domínio são propagados como exceções e tratados pelo middleware padrão de exceção/problem details.
- O tempo de expiração da reserva está fixo no domínio em 15 minutos.

## Comandos úteis

```bash
# Restaurar pacotes
dotnet restore

# Compilar
dotnet build

# Executar em desenvolvimento
dotnet run

# Executar com perfil HTTP
dotnet run --launch-profile http
```

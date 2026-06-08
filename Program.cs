using InventoryService.Api;
using InventoryService.Application;
using InventoryService.Application.Ports;
using InventoryService.Infrastructure.Outbox;
using InventoryService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("InventoryDb"));
});

builder.Services.AddScoped<InventoryApplicationService>();
builder.Services.AddScoped<ReservationApplicationService>();

builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IReservationRepository, ReservationRepository>();
builder.Services.AddScoped<IEventPublisher, OutboxEventPublisher>();
builder.Services.AddScoped<ITransactionRunner, EfCoreTransactionRunner>();

builder.Services.AddHostedService<ReservationExpirationWorker>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<InventoryDbContext>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapInventoryEndpoints();

app.Run();

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Vehicle_Data;
using Vehicle_Data.Models;

// Web application setup
var builder = WebApplication.CreateBuilder(args);

// Explicitly set the URLs for the application
builder.WebHost.UseUrls("https://localhost:5001", "http://localhost:5000");

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient();

// Configure API base URL
builder.Configuration["ApiBaseUrl"] = builder.Environment.IsDevelopment() 
    ? "https://localhost:5001" 
    : builder.Configuration["ApiBaseUrl"];

var app = builder.Build();

// Seed the database from CSV at startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    Vehicle_Data.InitializeDb.Initialize(db).Wait();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
else
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}

app.UseHttpsRedirection();
app.UseWebSockets();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// After app is built, enable Swagger middleware
app.UseSwagger();
app.UseSwaggerUI();

app.Run();

// CSV mapping class
public class VehicleCsv
{
    public int dealerId { get; set; }
    public required string vin { get; set; }
    public required string  modifiedDate { get; set; }
}

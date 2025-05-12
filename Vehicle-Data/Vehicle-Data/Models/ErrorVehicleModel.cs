namespace Vehicle_Data.Models;

using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class ErrorVehicleContext : DbContext
{
    public DbSet<ErrorVehicle> VehicleErrors { get; set; }

    public string DbPath { get; }

    public ErrorVehicleContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        DbPath = System.IO.Path.Join(path, "error_vehicles.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure the Id column as an auto-incrementing primary key
        modelBuilder.Entity<ErrorVehicle>()
            .Property(v => v.Id)
            .ValueGeneratedOnAdd(); // Ensure auto-increment behavior
    }
}

[Index(nameof(Vin), nameof(DealerId), nameof(ModifiedDate), IsUnique = true)]
public class ErrorVehicle
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Ensure auto-increment
    public int Id { get; set; } // Primary Key

    public required string Vin { get; set; }
    public required int DealerId { get; set; }
    public required DateOnly ModifiedDate { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorText { get; set; }
}

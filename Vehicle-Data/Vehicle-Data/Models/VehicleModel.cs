namespace Vehicle_Data.Models;

using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class VehicleContext : DbContext
{
    public DbSet<Vehicle> Vehicles { get; set; }

    public string DbPath { get; }

    public VehicleContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        DbPath = System.IO.Path.Join(path, "vehicles.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure the Id column as an auto-incrementing primary key
        modelBuilder.Entity<Vehicle>()
            .Property(v => v.Id)
            .ValueGeneratedOnAdd(); // Ensure auto-increment behavior
    }
}

[Index(nameof(Vin), nameof(DealerId), nameof(ModifiedDate), IsUnique = true)]
public class Vehicle
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // Ensure auto-increment
    public int Id { get; set; } // Primary Key

    public required string Vin { get; set; }
    public required int DealerId { get; set; }
    public required DateOnly ModifiedDate { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
}

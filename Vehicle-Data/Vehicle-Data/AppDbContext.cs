using Microsoft.EntityFrameworkCore;
using Vehicle_Data.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<VehicleModel> Vehicles { get; set; } = null!;
    public DbSet<ErrorVehicle> ErrorVehicles { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Add any custom model configuration here if needed
    }
} 
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Vehicle_Data.Models;

namespace Vehicle_Data.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly VehicleContext _context;

    public HomeController(ILogger<HomeController> logger, VehicleContext context)
    {
        _logger = logger;
        _context = context;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Vehicle_Table(int pageNumber = 1, int pageSize = 10, int? dealerId = null, DateOnly? modifiedDate = null)
    {
        var query = _context.Vehicles.AsQueryable();

        // Apply filtering
        if (dealerId.HasValue)
        {
            query = query.Where(v => v.DealerId == dealerId.Value);
        }

        if (modifiedDate.HasValue)
        {
            query = query.Where(v => v.ModifiedDate >= modifiedDate.Value);
        }

        // Apply pagination
        var totalRecords = query.Count();
        var vehicles = query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        ViewBag.PageNumber = pageNumber;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalRecords = totalRecords;
        ViewBag.DealerId = dealerId;
        ViewBag.ModifiedDate = modifiedDate;

        return View(vehicles);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

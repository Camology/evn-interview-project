using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Vehicle_Data.Models;
using System.Text.Json;
using System.Text;

namespace Vehicle_Data.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public HomeController(
        ILogger<HomeController> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public async Task<IActionResult> Vehicle_Table(int pageNumber = 1, int pageSize = 10, int? dealerId = null, DateOnly? modifiedDate = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = _configuration["ApiBaseUrl"] ?? "https://localhost:5001";
            
            // Build query string for filters
            var queryParams = new List<string>();
            if (dealerId.HasValue)
                queryParams.Add($"dealerId={dealerId.Value}");
            if (modifiedDate.HasValue)
                queryParams.Add($"modifiedDate={modifiedDate.Value:yyyy-MM-dd}");
            queryParams.Add($"pageNumber={pageNumber}");
            queryParams.Add($"pageSize={pageSize}");

            var response = await client.GetAsync($"{baseUrl}/api/vehicle?{string.Join("&", queryParams)}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch vehicles: {StatusCode}", response.StatusCode);
                return View(new List<VehicleModel>());
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PaginatedResult<VehicleModel>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            ViewBag.PageNumber = pageNumber;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalRecords = result?.TotalItems ?? 0;
            ViewBag.DealerId = dealerId;
            ViewBag.ModifiedDate = modifiedDate;

            return View(result?.Items ?? new List<VehicleModel>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching vehicles");
            return View(new List<VehicleModel>());
        }
    }

    public async Task<IActionResult> ErrorVehicle_Table(int pageNumber = 1, int pageSize = 10)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = _configuration["ApiBaseUrl"] ?? "https://localhost:5001";
            
            var response = await client.GetAsync($"{baseUrl}/api/vehicle/errors?pageNumber={pageNumber}&pageSize={pageSize}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch error vehicles: {StatusCode}", response.StatusCode);
                return View(new List<ErrorVehicleModel>());
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PaginatedResult<ErrorVehicle>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            ViewBag.PageNumber = pageNumber;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalRecords = result?.TotalItems ?? 0;

            // Map ErrorVehicle to ErrorVehicleModel
            var errorVehicleModels = result?.Items.Select(ev => new ErrorVehicleModel
            {
                DealerId = ev.DealerId,
                Vin = ev.Vin,
                ErrorMessage = ev.ErrorText ?? "Unknown error",
                ModifiedDate = ev.ModifiedDate.ToDateTime(TimeOnly.MinValue)
            }) ?? new List<ErrorVehicleModel>();

            return View(errorVehicleModels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching error vehicles");
            return View(new List<ErrorVehicleModel>());
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

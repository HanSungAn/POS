using Microsoft.AspNetCore.Mvc;
using PosSystem.Core.Models;

namespace PosSystem.HqApi.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public ActionResult<List<Product>> GetMasterProducts()
    {
        return Ok(new List<Product>
        {
            new() { Barcode = "880100000001", Name = "아메리카노 (ICE)", Category = "커피/음료", Price = 4500, StockQuantity = 120 },
            new() { Barcode = "880100000013", Name = "시즌 한정 말차 라떼", Category = "커피/음료", Price = 5900, StockQuantity = 50 },
            new() { Barcode = "880100000014", Name = "애플망고 스무디", Category = "커피/음료", Price = 6200, StockQuantity = 35 }
        });
    }
}

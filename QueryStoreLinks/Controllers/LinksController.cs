using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using QueryStoreLinks.Helpers;
using QueryStoreLinks.Models;
using StoreLib.Models;
using StoreLib.Services;

namespace QueryStoreLinks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LinksController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<LinksController> _logger;
        private static readonly MSHttpClient _httpClient = new MSHttpClient();

        public LinksController(IWebHostEnvironment env, ILogger<LinksController> logger)
        {
            _env = env;
            _logger = logger;
        }

        [HttpPost("resolve-all")]
        public async Task<ActionResult<ResolveAllResponse>> ResolveAll(
            [FromBody] ResolveAllRequest req,
            CancellationToken ct
        )
        {
            if (req == null)
                return BadRequest("Request body is required.");

            var result = new ResolveAllResponse();

            DisplayCatalogHandler dcathandler = new DisplayCatalogHandler(
                DCatEndpoint.Production,
                req.GetMappedLocale()
            );

            var idType = req.GetIdentifierType();
            var mappedLocale = req.GetMappedLocale();

            _logger.LogInformation(
                "Querying DCAT: Input={Input}, Type={Type}, Market={Market}",
                req.ProductInput,
                idType,
                mappedLocale.Market
            );

            await dcathandler.QueryDCATAsync(req.ProductInput, idType);

            var product =
                dcathandler.ProductListing?.Product
                ?? dcathandler.ProductListing?.Products?.FirstOrDefault();

            if (product == null)
            {
                _logger.LogWarning(
                    "No product found for Input={Input}, Type={Type}, Market={Market}",
                    req.ProductInput,
                    req.GetIdentifierType(),
                    req.GetMappedLocale().Market
                );
                return Ok(
                    new ResolveAllResponse { Errors = new List<string> { "Product not found." } }
                );
            }

            var localeProps = product.LocalizedProperties?.FirstOrDefault();
            var skuProps = product.DisplaySkuAvailabilities?.FirstOrDefault()?.Sku?.Properties;

            if (skuProps == null)
            {
                return Ok(
                    new ResolveAllResponse
                    {
                        Errors = new List<string> { "SKU properties not found." },
                    }
                );
            }

            AppInfo appInfo = new AppInfo
            {
                Name = localeProps?.ProductTitle ?? "Unknown Name",
                Publisher = localeProps?.PublisherName ?? "Unknown Publisher",
                Description = localeProps?.ProductDescription ?? string.Empty,
                CategoryId = skuProps?.FulfillmentData?.WuCategoryId,
                ProductId = product.ProductId,
            };

            var packageInstances = await dcathandler.GetPackagesForProductAsync();

            var downloadTasks = packageInstances.Select(async package =>
            {
                string fileSize = await PackageHelper.GetFileSizeAsync(
                    package.PackageUri.ToString(),
                    ct
                );

                return new DownloadItem
                {
                    FileName = package.PackageMoniker,
                    FileLink = package.PackageUri.ToString(),
                    FileSize = fileSize,
                };
            });

            var appxPackages = (await Task.WhenAll(downloadTasks)).ToList();

            return Ok(
                new ResolveAllResponse
                {
                    ProductId = product.ProductId,
                    AppInfo = appInfo,
                    AppxPackages = appxPackages,
                }
            );
        }
    }
}

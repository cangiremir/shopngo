using System.ComponentModel.DataAnnotations;
using FluentValidation;

namespace ShopNGo.StockService.Api;

public sealed class SeedStockRequest
{
    [Required]
    public List<SeedStockItemRequest> Items { get; set; } = [];
}

public sealed class SeedStockItemRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}

public sealed class SeedStockRequestValidator : AbstractValidator<SeedStockRequest>
{
    public SeedStockRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.ProductId).NotEmpty();
            item.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

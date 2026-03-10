using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebScrapingHub.Services
{
    public sealed record EexPriceRow(
        DateOnly Date,
        string Market,      // "power" ou "gas"
        string Area,        // ex: "FR" ou "PEG"
        string? Product,    // Base / Peak pour power ; null pour gas si pas de produit
        string Delivery,    // ex: 202701
        decimal Price
    );

}

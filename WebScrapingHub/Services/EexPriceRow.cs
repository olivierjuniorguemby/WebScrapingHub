using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebScrapingHub.Services
{
    public sealed record EexPriceRow(
    DateOnly Date,
    string Area,
    string Product,
    string Delivery,
    decimal Price
);

}

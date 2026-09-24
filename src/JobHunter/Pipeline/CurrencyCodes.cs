using System.Collections.Frozen;

namespace JobHunter.Pipeline;

/// <summary>The currency codes in circulation under ISO 4217, which tell a currency code apart from any other three capital letters in a posting or a kit.</summary>
/// <remarks>Fund codes, precious metals and testing codes are left out: a posting never states pay in them.</remarks>
public static class CurrencyCodes
{
    private static readonly FrozenSet<string> Circulating = FrozenSet.ToFrozenSet(
    [
        "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN",
        "BAM", "BBD", "BDT", "BGN", "BHD", "BIF", "BMD", "BND", "BOB", "BRL", "BSD", "BTN", "BWP", "BYN", "BZD",
        "CAD", "CDF", "CHF", "CLP", "CNY", "COP", "CRC", "CUP", "CVE", "CZK",
        "DJF", "DKK", "DOP", "DZD",
        "EGP", "ERN", "ETB", "EUR",
        "FJD", "FKP",
        "GBP", "GEL", "GHS", "GIP", "GMD", "GNF", "GTQ", "GYD",
        "HKD", "HNL", "HTG", "HUF",
        "IDR", "ILS", "INR", "IQD", "IRR", "ISK",
        "JMD", "JOD", "JPY",
        "KES", "KGS", "KHR", "KMF", "KPW", "KRW", "KWD", "KYD", "KZT",
        "LAK", "LBP", "LKR", "LRD", "LSL", "LYD",
        "MAD", "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR", "MWK", "MXN", "MYR", "MZN",
        "NAD", "NGN", "NIO", "NOK", "NPR", "NZD",
        "OMR",
        "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "PYG",
        "QAR",
        "RON", "RSD", "RUB", "RWF",
        "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP", "SLE", "SOS", "SRD", "SSP", "STN", "SVC", "SYP", "SZL",
        "THB", "TJS", "TMT", "TND", "TOP", "TRY", "TTD", "TWD", "TZS",
        "UAH", "UGX", "USD", "UYU", "UZS",
        "VES", "VND", "VUV",
        "WST",
        "XAF", "XCD", "XCG", "XOF", "XPF",
        "YER",
        "ZAR", "ZMW", "ZWG"
    ], StringComparer.Ordinal);

    /// <summary>True when the text is a circulating currency code written the way codes are written, in capitals.</summary>
    public static bool IsCirculating(string code)
    {
        return Circulating.Contains(code);
    }
}

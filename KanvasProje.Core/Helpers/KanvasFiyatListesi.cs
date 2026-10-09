namespace KanvasProje.Core.Helpers;

public readonly record struct KanvasFiyati(decimal CercevesizKargoDahil, decimal CerceveliKargoDahil);

public static class KanvasFiyatListesi
{
    private static readonly IReadOnlyDictionary<string, KanvasFiyati> Fiyatlar = new Dictionary<string, KanvasFiyati>(StringComparer.OrdinalIgnoreCase)
    {
        ["10X30"] = new(433.76M, 684.04M), ["18X25"] = new(457.03M, 715.06M),
        ["20X30"] = new(700M, 1400M), ["20X50"] = new(603.39M, 923.46M),
        ["20X60"] = new(900M, 2000M), ["25X25"] = new(675M, 1400M),
        ["25X35"] = new(750M, 1650M), ["25X60"] = new(712.81M, 1067.42M),
        ["25X75"] = new(1100M, 2400M), ["30X30"] = new(750M, 1650M),
        ["30X35"] = new(596.34M, 905.83M), ["30X45"] = new(671.21M, 1003.97M),
        ["30X60"] = new(1050M, 2250M), ["30X70"] = new(832.55M, 1223.83M),
        ["30X90"] = new(1300M, 2850M), ["35X50"] = new(1000M, 2150M),
        ["35X70"] = new(882.61M, 1287.98M), ["35X80"] = new(963.90M, 1392.54M),
        ["40X40"] = new(950M, 2050M), ["40X50"] = new(797.30M, 1167.43M),
        ["40X60"] = new(1150M, 2500M), ["40X80"] = new(1350M, 2950M),
        ["40X120"] = new(1800M, 3850M), ["45X90"] = new(1157.70M, 1639.22M),
        ["45X100"] = new(1240.88M, 1746.36M), ["50X50"] = new(1200M, 2550M),
        ["50X70"] = new(1400M, 3000M), ["50X100"] = new(1750M, 3650M),
        ["50X110"] = new(1431.24M, 1976.21M), ["50X120"] = new(1520.68M, 2091.73M),
        ["50X150"] = new(2300M, 4850M), ["55X100"] = new(1384.01M, 1919.10M),
        ["60X40"] = new(1150M, 2500M), ["60X60"] = new(1400M, 3000M),
        ["60X90"] = new(1800M, 3700M),
        ["60X100"] = new(1950M, 3950M), ["60X120"] = new(2200M, 4550M),
        ["60X140"] = new(1873.78M, 2527.32M), ["60X150"] = new(1962.61M, 2641.53M),
        ["60X180"] = new(2900M, 6050M), ["60X200"] = new(2438.94M, 3246.87M),
        ["70X35"] = new(882.61M, 1287.98M), ["70X70"] = new(1700M, 3500M),
        ["70X100"] = new(2100M, 4250M),
        ["70X120"] = new(2350M, 4750M), ["70X140"] = new(2650M, 5400M),
        ["70X150"] = new(2158.55M, 2869.90M), ["70X160"] = new(2273.99M, 3012.83M),
        ["75X75"] = new(1850M, 3775M), ["75X90"] = new(1563.66M, 2129.07M),
        ["75X120"] = new(1935.82M, 2582.31M), ["75X175"] = new(2550.71M, 3346.66M),
        ["80X80"] = new(2000M, 4050M), ["80X110"] = new(1906.21M, 2541.42M),
        ["80X120"] = new(2600M, 5250M), ["80X150"] = new(2372.99M, 3117.48M),
        ["80X160"] = new(3200M, 6400M), ["85X150"] = new(2474.90M, 3235.59M),
        ["90X60"] = new(1800M, 3700M), ["90X90"] = new(2250M, 4600M),
        ["90X110"] = new(2058.44M, 2723.25M),
        ["90X120"] = new(2195.73M, 2888.75M), ["90X130"] = new(3000M, 5900M),
        ["90X140"] = new(2450.22M, 3199.64M), ["90X150"] = new(2577.50M, 3355.12M),
        ["90X170"] = new(2845.56M, 3678.87M), ["90X180"] = new(3850M, 7450M),
        ["100X50"] = new(1750M, 3650M), ["100X60"] = new(1950M, 3950M),
        ["100X100"] = new(2600M, 5200M),
        ["100X130"] = new(2506.30M, 3258.53M),
        ["100X150"] = new(3600M, 6950M), ["100X160"] = new(2937.20M, 3776.15M),
        ["100X180"] = new(3230.49M, 4127.25M), ["100X235"] = new(4101.91M, 5158.70M),
        ["109X148"] = new(2950.60M, 3784.61M), ["110X110"] = new(3100M, 6000M),
        ["110X120"] = new(2534.50M, 3288.85M), ["110X160"] = new(4000M, 7800M),
        ["110X180"] = new(3474.10M, 4406.82M), ["110X200"] = new(3790.11M, 4782.05M),
        ["110X250"] = new(4680.56M, 5820.55M), ["120X120"] = new(3500M, 6700M),
        ["120X125"] = new(2787.17M, 3587.34M), ["120X150"] = new(3219.91M, 4096.23M),
        ["120X60"] = new(2200M, 4550M), ["120X70"] = new(2350M, 4750M),
        ["120X80"] = new(2600M, 5250M),
        ["120X160"] = new(3382.28M, 4288.91M), ["120X180"] = new(4750M, 8850M),
        ["130X130"] = new(3064.72M, 3910.72M), ["140X70"] = new(2650M, 5400M),
        ["140X140"] = new(3448.72M, 4357.47M),
        ["140X200"] = new(4628.39M, 5730.31M), ["140X290"] = new(6511.96M, 7902.93M),
        ["150X60"] = new(1962.61M, 2641.53M), ["150X70"] = new(2158.55M, 2869.90M),
        ["150X90"] = new(2577.50M, 3355.12M), ["150X100"] = new(3600M, 6950M),
        ["150X150"] = new(3859.65M, 4833.96M),
        ["150X250"] = new(6069.56M, 7373.10M),
        ["150X280"] = new(6701.09M, 8102.63M), ["150X290"] = new(6911.36M, 8345.33M),
        ["150X300"] = new(7121.64M, 8589.45M), ["153X204"] = new(5193.91M, 6357.16M),
        ["160X80"] = new(3200M, 6400M), ["80X160"] = new(3200M, 6400M),
        ["180X90"] = new(3850M, 7450M), ["90X180"] = new(3850M, 7450M),
        ["180X120"] = new(4750M, 8850M), ["180X200"] = new(5750.00M, 6999.26M),
        ["250X150"] = new(6069.56M, 7373.10M),
        // 2:1 Yatay Kanvas Fiyatları (Varyasyon 2)
        ["20X40"] = new(750M, 1600M), ["40X20"] = new(750M, 1600M),
        ["25X50"] = new(900M, 1950M), ["50X25"] = new(900M, 1950M),
        ["30X60"] = new(1050M, 2250M), ["60X30"] = new(1050M, 2250M),
        ["40X80"] = new(1350M, 2950M), ["80X40"] = new(1350M, 2950M),
        ["50X100"] = new(1750M, 3650M), ["100X50"] = new(1750M, 3650M),
        ["60X120"] = new(2200M, 4550M), ["120X60"] = new(2200M, 4550M),
        ["70X140"] = new(2650M, 5400M), ["140X70"] = new(2650M, 5400M),
        ["100X200"] = new(4500M, 8600M), ["200X100"] = new(4500M, 8600M),
        ["35X35"] = new(850M, 1850M),
        ["100X65"] = new(2050M, 4100M),
        ["65X100"] = new(2050M, 4100M),
        ["130X85"] = new(2850M, 5600M),
        ["85X130"] = new(2850M, 5600M),
    };

    public static bool TryGetFiyat(string? olcu, out KanvasFiyati fiyat)
    {
        var norm = NormalizeOlcu(olcu);
        if (Fiyatlar.TryGetValue(norm, out fiyat))
        {
            return true;
        }

        var parts = norm.Split('X');
        if (parts.Length == 2)
        {
            var reversed = $"{parts[1]}X{parts[0]}";
            if (Fiyatlar.TryGetValue(reversed, out fiyat))
            {
                return true;
            }
        }

        fiyat = default;
        return false;
    }

    public static string NormalizeOlcu(string? olcu) => (olcu ?? string.Empty)
        .Trim()
        .ToUpperInvariant()
        .Replace("CM", string.Empty)
        .Replace(" ", string.Empty)
        .Replace("×", "X");
}

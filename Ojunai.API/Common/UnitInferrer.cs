namespace Ojunai.API.Common;

public static class UnitInferrer
{
    private static readonly List<(string Unit, string[] Keywords)> Map = new()
    {
        ("bag", new[] { "rice", "beans", "garri", "cement", "flour", "sand", "gravel", "yam", "wheat", "sugar", "salt", "feed", "fertilizer", "maize", "corn", "millet", "sorghum", "groundnut", "pounded yam", "semolina", "oats", "couscous" }),
        ("bottle", new[] { "shampoo", "conditioner", "perfume", "lotion", "cream", "oil", "serum", "spray", "wine", "juice", "water", "drink", "syrup", "cologne", "body wash", "mouthwash", "aftershave", "sanitizer", "detergent", "bleach" }),
        ("tube", new[] { "toothpaste", "glue", "ointment", "gel", "lubricant" }),
        ("pack", new[] { "diaper", "nappy", "wipe", "sachet", "sanitary pad", "tampon", "tissue", "noodle", "indomie", "biscuit", "battery", "condom", "gum", "band aid", "plaster", "cotton wool", "spaghetti", "pasta" }),
        ("box", new[] { "carton", "cereal", "tea bag", "match", "crayon", "chalk" }),
        ("tin", new[] { "sardine", "canned", "tomato paste", "milk tin", "baked beans", "corned beef" }),
        ("pair", new[] { "shoe", "sneaker", "sandal", "boot", "slipper", "heel", "earring", "sock", "glove", "lash", "eyelash" }),
        ("set", new[] { "kit", "brush set", "tool set", "cutlery", "nail kit", "makeup set", "nail set", "first aid" }),
        ("roll", new[] { "toilet paper", "tape", "wrap", "foil", "fabric", "cling film", "bubble wrap", "roll on" }),
        ("piece", new[] { "phone", "laptop", "speaker", "charger", "cable", "bulb", "hammer", "drill", "chair", "table", "mirror", "clock", "frame", "lamp", "pillow", "roller", "derma", "sponge", "blender", "tweezer", "comb", "brush" }),
        ("kg", new[] { "chicken", "beef", "fish", "meat", "goat", "pork", "turkey", "prawn", "shrimp", "crayfish", "cooking gas", "gas cylinder" }),
        ("crate", new[] { "egg", "beer crate", "malt crate", "drink crate" }),
        ("loaf", new[] { "bread", "agege bread" }),
        ("litre", new[] { "kerosene", "petrol", "diesel", "fuel", "engine oil", "palm oil" }),
        ("yard", new[] { "ankara", "fabric", "lace", "cloth", "linen", "satin", "silk", "chiffon" }),
        ("strip", new[] { "tablet", "capsule", "medicine", "drug", "paracetamol", "ibuprofen" }),
        ("bucket", new[] { "paint" }),
        ("meter", new[] { "rope", "wire", "cable", "hose", "chain" }),
    };

    public static string Infer(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return "piece";
        var lower = productName.ToLowerInvariant();

        foreach (var (unit, keywords) in Map)
        {
            foreach (var keyword in keywords)
            {
                if (lower.Contains(keyword))
                    return unit;
            }
        }

        return "piece"; // safe default for countable items
    }
}

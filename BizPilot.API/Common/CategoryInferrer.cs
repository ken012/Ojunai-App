using System.Text.RegularExpressions;

namespace BizPilot.API.Common;

public static class CategoryInferrer
{
    private static readonly List<(string Category, string Subcategory, string[] Keywords)> Map = new()
    {
        // Jewelry & Accessories — MUST be before other categories to prevent false matches
        ("Jewelry & Accessories", "Rings", new[] { " ring", "signet ring", "engagement ring", "wedding ring" }),
        ("Jewelry & Accessories", "Necklaces", new[] { "necklace", "chain", "choker", "pendant" }),
        ("Jewelry & Accessories", "Earrings", new[] { "earring", "stud", "hoop earring" }),
        ("Jewelry & Accessories", "Bracelets", new[] { "bracelet", "bangle", "wristband", "charm bracelet" }),
        ("Jewelry & Accessories", "Watches", new[] { "watch", "wristwatch", "timepiece" }),
        ("Jewelry & Accessories", "Anklets", new[] { "anklet", "ankle chain", "ankle bracelet" }),
        ("Jewelry & Accessories", "Brooches & Pins", new[] { "brooch", "lapel pin", "badge pin" }),
        ("Jewelry & Accessories", "Cufflinks & Tie", new[] { "cufflink", "tie clip", "tie pin" }),
        ("Jewelry & Accessories", "Gemstones", new[] { "diamond", "ruby", "emerald", "sapphire", "amethyst", "opal", "topaz", "pearl", "garnet", "turquoise", "onyx", "quartz" }),
        ("Jewelry & Accessories", "Precious Metals", new[] { "gold ", "silver ", "platinum ", "rose gold", "sterling", "14k", "18k", "24k", "karat" }),
        ("Jewelry & Accessories", "Fashion Accessories", new[] { "jewelry", "jewellery", "accessory", "accessories", "trinket", "ornament" }),

        // Food & Beverages
        ("Food & Beverages", "Grains & Rice", new[] { "rice", "garri", "wheat", "flour", "semolina", "oats", "couscous", "millet", "sorghum", "maize", "corn", "beans" }),
        ("Food & Beverages", "Snacks", new[] { "biscuit", "cookie", "chips", "chin chin", "puff puff", "popcorn", "crackers", "nuts", "candy", "chocolate", "sweet", "snack" }),
        ("Food & Beverages", "Drinks", new[] { "juice", "water", "soda", "coke", "fanta", "pepsi", "malt", "zobo", "yogurt", "milk drink", "beer", "wine", "spirit", "drink" }),
        ("Food & Beverages", "Frozen Foods", new[] { "frozen", "ice cream" }),
        ("Food & Beverages", "Dairy Products", new[] { "milk", "cheese", "butter", "yoghurt", "egg" }),
        ("Food & Beverages", "Meat & Fish", new[] { "meat", "beef", "chicken", "fish", "turkey", "goat", "pork", "suya", "kilishi", "crayfish", "prawn", "shrimp" }),
        ("Food & Beverages", "Spices & Seasoning", new[] { "pepper", "maggi", "knorr", "curry", "thyme", "ginger", "garlic", "onion", "tomato paste", "seasoning", "spice" }),
        ("Food & Beverages", "Condiments & Sauces", new[] { "ketchup", "mayonnaise", "mayo", "mustard", "sauce", "vinegar", "salad cream", "salad dressing", "soy sauce", "chili sauce", "hot sauce" }),
        ("Food & Beverages", "Tea & Coffee", new[] { "tea bag", "coffee", "cocoa", "milo", "bournvita", "ovaltine", "green tea", "lipton", "nescafe" }),
        ("Food & Beverages", "Sugar & Sweeteners", new[] { "sugar", "honey", "syrup", "sweetener", "brown sugar", "cube sugar" }),
        ("Food & Beverages", "Canned & Packaged Goods", new[] { "sardine", "canned", "noodle", "indomie", "spaghetti", "pasta", "baked beans", "tin fish", "tin tomato" }),
        ("Food & Beverages", "Bakery Items", new[] { "bread", "cake", "pastry", "doughnut", "croissant", "muffin" }),
        ("Food & Beverages", "Produce", new[] { "yam", "plantain", "cassava", "potato", "vegetable", "carrot", "cabbage", "lettuce", "palm oil", "groundnut", "pounded yam" }),

        // Beauty & Personal Care
        ("Beauty & Personal Care", "Hair Products", new[] { "shampoo", "conditioner", "hair cream", "relaxer", "hair oil", "wig", "weave", "braid", "hair food" }),
        ("Beauty & Personal Care", "Skin Care", new[] { "lotion", "moisturizer", "skin care", "sunscreen", "serum", "toner", "face wash", "body cream", "night cream", "derma roller", "derma", "facial roller", "jade roller", "gua sha" }),
        ("Beauty & Personal Care", "Makeup", new[] { "foundation", "lipstick", "lip gloss", "lip liner", "mascara", "eyeliner", "eyebrow", "powder", "concealer", "primer", "blush", "highlighter", "setting spray", "beauty blender", "brush set", "false eyelash", "compact", "makeup" }),
        ("Beauty & Personal Care", "Fragrances", new[] { "perfume", "cologne", "body spray", "deodorant", "roll on", "fragrance" }),
        ("Beauty & Personal Care", "Body Care", new[] { "body wash", "shower gel", "body scrub", "bath bomb", "soap" }),
        ("Beauty & Personal Care", "Grooming", new[] { "razor", "shaving cream", "beard oil", "clipper", "trimmer", "aftershave" }),
        ("Beauty & Personal Care", "Hair Tools & Accessories", new[] { "comb", "hair brush", "hairbrush", "hair clip", "hair band", "hair pin", "dryer", "straightener", "curler" }),
        ("Beauty & Personal Care", "Nails", new[] { "nail polish", "nail kit", "nail set", "press on nail", "acrylic nail", "gel nail", "nail file", "nail glue", "cuticle" }),

        // Health & Wellness
        ("Health & Wellness", "Supplements", new[] { "vitamin", "supplement", "omega", "protein", "collagen" }),
        ("Health & Wellness", "Medical Supplies", new[] { "bandage", "plaster", "syringe", "glove", "thermometer", "first aid", "medical" }),
        ("Health & Wellness", "First Aid", new[] { "antiseptic", "dettol", "iodine", "cotton wool" }),
        ("Health & Wellness", "Personal Hygiene", new[] { "toothpaste", "toothbrush", "mouthwash", "dental floss", "sanitary pad", "tampon", "tissue", "toilet paper", "wipe", "diaper" }),
        ("Health & Wellness", "Fitness Nutrition", new[] { "whey", "creatine", "pre workout", "energy bar" }),

        // Clothing & Apparel
        ("Clothing & Apparel", "Men's Wear", new[] { "shirt", "trouser", "suit", "tie", "cap", "agbada", "kaftan", "senator", "jeans", "jean", "t-shirt", "polo", "shorts" }),
        ("Clothing & Apparel", "Women's Wear", new[] { "dress", "blouse", "skirt", "gown", "wrapper", "ankara", "lace" }),
        ("Clothing & Apparel", "Kids Wear", new[] { "baby cloth", "children wear", "onesie" }),
        ("Clothing & Apparel", "Shoes", new[] { "shoe", "sandal", "sneaker", "boot", "slipper", "heel" }),
        ("Clothing & Apparel", "Underwear & Sleepwear", new[] { "boxer", "bra", "panties", "underwear", "pyjama", "nightgown", "lingerie" }),
        ("Clothing & Apparel", "Workwear", new[] { "overall", "uniform", "apron", "safety boot", "jacket", "vest", "hi vis" }),

        // Electronics
        ("Electronics", "Phones & Accessories", new[] { "phone", "case", "screen protector", "earphone", "headphone", "airpod", "power bank" }),
        ("Electronics", "Computers & Laptops", new[] { "laptop", "computer", "keyboard", "mouse", "monitor", "usb", "flash drive", "hard drive" }),
        ("Electronics", "Audio Devices", new[] { "speaker", "bluetooth", "radio", "microphone" }),
        ("Electronics", "TVs & Displays", new[] { "tv", "television", "remote", "hdmi" }),
        ("Electronics", "Gaming", new[] { "controller", "game pad", "console", "playstation", "xbox" }),
        ("Electronics", "Smart Devices", new[] { "smart watch", "tracker", "camera", "ring light" }),
        ("Electronics", "Chargers & Cables", new[] { "charger", "cable", "adapter", "converter", "extension", "socket" }),

        // Home & Kitchen
        ("Home & Kitchen", "Cookware", new[] { "pot", "pan", "frying", "kettle", "cooker", "stove", "oven", "microwave", "blender", "mixer" }),
        ("Home & Kitchen", "Kitchen Appliances", new[] { "fridge", "freezer", "dispenser", "toaster", "juicer" }),
        ("Home & Kitchen", "Home Decor", new[] { "curtain", "pillow", "frame", "vase", "rug", "mat", "clock", "mirror", "lamp" }),
        ("Home & Kitchen", "Furniture", new[] { "chair", "table", "shelf", "bed", "wardrobe", "cabinet", "desk", "stool" }),
        ("Home & Kitchen", "Cleaning Supplies", new[] { "detergent", "bleach", "omo", "ariel", "mop", "broom", "bucket", "sponge", "duster", "air freshener", "freshener", "disinfectant", "febreeze", "glade" }),
        ("Home & Kitchen", "Storage & Organization", new[] { "basket", "bin", "container", "rack", "hanger" }),
        ("Home & Kitchen", "Bedding", new[] { "sheet", "duvet", "blanket", "mattress", "pillow case" }),

        // Office & Stationery
        ("Office & Stationery", "Notebooks", new[] { "notebook", "journal", "diary", "exercise book", "jotter" }),
        ("Office & Stationery", "Pens & Writing Tools", new[] { "pen", "pencil", "marker", "highlighter", "eraser", "sharpener", "ruler" }),
        ("Office & Stationery", "Printing Supplies", new[] { "paper", "ink", "toner", "cartridge", "a4" }),
        ("Office & Stationery", "Office Equipment", new[] { "stapler", "punch", "tape", "scissors", "calculator", "file", "folder", "envelope" }),
        ("Office & Stationery", "School Supplies", new[] { "backpack", "school bag", "lunch box", "crayon", "color pencil" }),

        // Baby & Kids
        ("Baby & Kids", "Baby Food", new[] { "cereal", "formula", "baby food", "pap" }),
        ("Baby & Kids", "Diapers & Wipes", new[] { "diaper", "nappy", "pampers", "huggies" }),
        ("Baby & Kids", "Toys", new[] { "toy", "doll", "lego", "puzzle", "teddy" }),
        ("Baby & Kids", "Baby Care", new[] { "baby oil", "baby powder", "baby cream", "baby soap", "bottle", "pacifier", "teether" }),
        ("Baby & Kids", "School Items", new[] { "pencil case" }),

        // Agriculture & Farming
        ("Agriculture & Farming", "Seeds", new[] { "seed", "seedling" }),
        ("Agriculture & Farming", "Fertilizers", new[] { "fertilizer", "manure", "npk", "urea" }),
        ("Agriculture & Farming", "Equipment", new[] { "hoe", "cutlass", "wheelbarrow", "sprayer", "rake" }),
        ("Agriculture & Farming", "Animal Feed", new[] { "feed", "poultry feed", "fish feed", "hay", "silage" }),
        ("Agriculture & Farming", "Pesticides", new[] { "pesticide", "herbicide", "insecticide", "fungicide" }),

        // Tools & Hardware
        ("Tools & Hardware", "Hand Tools", new[] { "hammer", "screwdriver", "pliers", "wrench", "spanner", "saw", "tape measure" }),
        ("Tools & Hardware", "Power Tools", new[] { "drill", "grinder", "sander", "jigsaw", "circular saw" }),
        ("Tools & Hardware", "Building Materials", new[] { "cement", "block", "sand", "gravel", "rod", "iron", "nail", "screw", "bolt", "wood", "plank", "roofing", "zinc", "paint" }),
        ("Tools & Hardware", "Electrical Supplies", new[] { "wire", "bulb", "switch", "breaker", "fuse", "led" }),
        ("Tools & Hardware", "Plumbing Supplies", new[] { "pipe", "tap", "valve", "tank", "pump", "hose", "fitting" }),

        // Industrial & Bulk Supplies
        ("Industrial & Bulk Supplies", "Packaging Materials", new[] { "sack", "carton", "wrap", "nylon", "polythene", "bubble wrap" }),
        ("Industrial & Bulk Supplies", "Wholesale Goods", new[] { "wholesale", "bulk" }),
        ("Industrial & Bulk Supplies", "Raw Materials", new[] { "rubber", "plastic", "chemical", "resin" }),
    };

    // Short keywords that must match as whole words to avoid false positives
    // e.g., "pen" should not match "pendant", "bra" should not match "bracelet"
    private static readonly HashSet<string> WholeWordOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "pen", "bra", "ink", "tin", "tie", "cap", "mat", "tap", "rod", "saw",
        "mop", "bin", "fan", "led", "egg", "pie", "tea", "oil", "gel", "salt",
        "cream", "lace", "ring"
    };

    private static bool MatchesKeyword(string text, string keyword)
    {
        if (keyword.Length <= 4 || WholeWordOnly.Contains(keyword.Trim()))
        {
            // Use word boundary matching for short/ambiguous keywords
            var pattern = @"(?<![a-z])" + Regex.Escape(keyword.Trim()) + @"(?![a-z])";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        }
        return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    public static (string? Category, string? Subcategory) Infer(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return (null, null);
        var lower = " " + productName.ToLowerInvariant() + " ";

        foreach (var (category, subcategory, keywords) in Map)
        {
            foreach (var keyword in keywords)
            {
                if (MatchesKeyword(lower, keyword))
                    return (category, subcategory);
            }
        }

        return (null, null);
    }
}

using System.Text.RegularExpressions;

namespace Ojunai.API.Common;

public static class CategoryInferrer
{
    private static readonly List<(string Category, string Subcategory, string[] Keywords)> Map = new()
    {
        // Jewelry & Accessories — checked FIRST to prevent false matches from gemstone/metal names
        ("Jewelry & Accessories", "Necklaces", new[] { "necklace", "choker", "pendant" }),
        ("Jewelry & Accessories", "Earrings", new[] { "earring" }),
        ("Jewelry & Accessories", "Bracelets", new[] { "bracelet", "bangle", "charm bracelet" }),
        ("Jewelry & Accessories", "Rings", new[] { "ring" }),
        ("Jewelry & Accessories", "Watches", new[] { "watch", "wristwatch", "timepiece" }),
        ("Jewelry & Accessories", "Anklets", new[] { "anklet", "ankle chain" }),
        ("Jewelry & Accessories", "Brooches & Pins", new[] { "brooch", "lapel pin" }),
        ("Jewelry & Accessories", "Cufflinks & Tie", new[] { "cufflink", "tie clip", "tie pin" }),
        ("Jewelry & Accessories", "Gemstones", new[] { "diamond", "ruby", "emerald", "sapphire", "amethyst", "opal", "topaz", "pearl", "garnet", "turquoise", "onyx", "quartz" }),
        ("Jewelry & Accessories", "Precious Metals", new[] { "gold", "silver", "platinum", "rose gold", "sterling", "14k", "18k", "24k", "karat" }),
        ("Jewelry & Accessories", "Fashion Accessories", new[] { "jewelry", "jewellery", "trinket", "ornament" }),

        // Beauty & Personal Care — before Food so "shea butter" matches Beauty, not Dairy
        ("Beauty & Personal Care", "Hair Products", new[] { "shampoo", "hair conditioner", "hair cream", "relaxer", "hair oil", "wig", "weave", "braid", "hair food", "hair extension" }),
        ("Beauty & Personal Care", "Skin Care", new[] { "lotion", "moisturizer", "skin care", "sunscreen", "sunblock", "skin toner", "face toner", "face wash", "body cream", "night cream", "derma roller", "derma", "facial roller", "jade roller", "gua sha", "shea butter", "cocoa butter" }),
        ("Beauty & Personal Care", "Makeup", new[] { "foundation", "lipstick", "lip gloss", "lip liner", "mascara", "eyeliner", "eyebrow", "concealer", "primer", "blush", "highlighter", "setting spray", "beauty blender", "brush set", "false eyelash", "compact", "makeup", "face paint", "powder" }),
        ("Beauty & Personal Care", "Fragrances", new[] { "perfume", "cologne", "body spray", "deodorant", "roll on", "fragrance" }),
        ("Beauty & Personal Care", "Body Care", new[] { "body wash", "shower gel", "body scrub", "bath bomb", "soap" }),
        ("Beauty & Personal Care", "Grooming", new[] { "razor", "shaving cream", "beard oil", "clipper", "trimmer", "aftershave" }),
        ("Beauty & Personal Care", "Hair Tools & Accessories", new[] { "comb", "hair brush", "hairbrush", "hair clip", "hair band", "hair pin", "hair dryer", "straightener", "curler" }),
        ("Beauty & Personal Care", "Nails", new[] { "nail polish", "nail kit", "nail set", "press on nail", "acrylic nail", "gel nail", "nail file", "nail glue", "nail art", "cuticle" }),
        ("Beauty & Personal Care", "Serum", new[] { "serum" }),

        // Baby & Kids — before Food/Health so "baby powder" matches Baby, not Makeup
        ("Baby & Kids", "Baby Food", new[] { "cereal", "baby formula", "baby food", "pap" }),
        ("Baby & Kids", "Diapers & Wipes", new[] { "diaper", "nappy", "pampers", "huggies" }),
        ("Baby & Kids", "Toys", new[] { "toy", "doll", "lego", "puzzle", "teddy" }),
        ("Baby & Kids", "Baby Care", new[] { "baby oil", "baby powder", "baby cream", "baby soap", "baby bottle", "feeding bottle", "pacifier", "teether" }),
        ("Baby & Kids", "School Items", new[] { "pencil case" }),

        // Food & Beverages
        ("Food & Beverages", "Grains & Rice", new[] { "rice", "garri", "wheat", "flour", "semolina", "oats", "couscous", "millet", "sorghum", "maize", "corn", "beans" }),
        ("Food & Beverages", "Snacks", new[] { "biscuit", "cookie", "chips", "chin chin", "puff puff", "popcorn", "crackers", "nuts", "candy", "chocolate", "sweet", "snack" }),
        ("Food & Beverages", "Drinks", new[] { "juice", "water", "soda", "coke", "fanta", "pepsi", "malt", "zobo", "yogurt", "milk drink", "beer", "wine", "spirits", "drink" }),
        ("Food & Beverages", "Frozen Foods", new[] { "frozen", "ice cream" }),
        ("Food & Beverages", "Dairy Products", new[] { "milk", "cheese", "butter", "yoghurt", "egg" }),
        ("Food & Beverages", "Meat & Fish", new[] { "meat", "beef", "chicken", "fish", "turkey", "goat", "pork", "suya", "kilishi", "crayfish", "prawn", "shrimp" }),
        ("Food & Beverages", "Spices & Seasoning", new[] { "pepper", "maggi", "knorr", "curry", "thyme", "ginger", "garlic", "onion", "tomato paste", "seasoning", "spice" }),
        ("Food & Beverages", "Condiments & Sauces", new[] { "ketchup", "mayonnaise", "mayo", "mustard", "sauce", "vinegar", "salad cream", "soy sauce", "chili sauce", "hot sauce" }),
        ("Food & Beverages", "Tea & Coffee", new[] { "tea bag", "coffee beans", "instant coffee", "ground coffee", "cocoa", "milo", "bournvita", "ovaltine", "green tea", "lipton", "nescafe" }),
        ("Food & Beverages", "Sugar & Sweeteners", new[] { "sugar", "honey", "syrup", "sweetener" }),
        ("Food & Beverages", "Canned & Packaged Goods", new[] { "sardine", "canned", "noodle", "indomie", "spaghetti", "pasta", "baked beans", "tin fish", "tin tomato" }),
        ("Food & Beverages", "Bakery Items", new[] { "bread", "cake", "pastry", "doughnut", "croissant", "muffin" }),
        ("Food & Beverages", "Produce", new[] { "yam", "plantain", "cassava", "potato", "vegetable", "carrot", "cabbage", "lettuce", "palm oil", "groundnut", "pounded yam" }),

        // Health & Wellness
        ("Health & Wellness", "Supplements", new[] { "vitamin", "supplement", "omega", "protein", "collagen" }),
        ("Health & Wellness", "Medical Supplies", new[] { "bandage", "plaster", "syringe", "glove", "thermometer", "first aid", "medical" }),
        ("Health & Wellness", "First Aid", new[] { "antiseptic", "dettol", "iodine", "cotton wool" }),
        ("Health & Wellness", "Personal Hygiene", new[] { "toothpaste", "toothbrush", "mouthwash", "dental floss", "sanitary pad", "tampon", "tissue", "toilet paper", "wipe" }),
        ("Health & Wellness", "Fitness Nutrition", new[] { "whey", "creatine", "pre workout", "energy bar" }),

        // Clothing & Apparel
        ("Clothing & Apparel", "Men's Wear", new[] { "shirt", "trouser", "suit", "tie", "cap", "agbada", "kaftan", "senator", "jeans", "jean", "t-shirt", "polo", "shorts" }),
        ("Clothing & Apparel", "Women's Wear", new[] { "dress", "blouse", "skirt", "gown", "wrapper", "ankara", "lace" }),
        ("Clothing & Apparel", "Kids Wear", new[] { "baby cloth", "children wear", "onesie" }),
        ("Clothing & Apparel", "Shoes", new[] { "shoe", "sandal", "sneaker", "boot", "slipper", "heel" }),
        ("Clothing & Apparel", "Underwear & Sleepwear", new[] { "boxer", "bra", "panties", "underwear", "pyjama", "nightgown", "lingerie" }),
        ("Clothing & Apparel", "Workwear", new[] { "overall", "uniform", "apron", "safety boot", "jacket", "vest", "hi vis" }),

        // Electronics
        ("Electronics", "Phones & Accessories", new[] { "phone", "phone case", "screen protector", "earphone", "headphone", "airpod", "power bank" }),
        ("Electronics", "Computers & Laptops", new[] { "laptop", "computer", "keyboard", "mouse", "monitor", "usb", "flash drive", "hard drive", "tablet" }),
        ("Electronics", "Audio Devices", new[] { "speaker", "bluetooth", "radio", "microphone" }),
        ("Electronics", "TVs & Displays", new[] { "tv", "television", "remote control", "hdmi" }),
        ("Electronics", "Gaming", new[] { "controller", "game pad", "console", "playstation", "xbox", "nintendo" }),
        ("Electronics", "Smart Devices", new[] { "smart watch", "tracker", "camera", "ring light" }),
        ("Electronics", "Chargers & Cables", new[] { "charger", "cable", "adapter", "converter", "extension cord", "extension box", "socket" }),

        // Home & Kitchen
        ("Home & Kitchen", "Cookware", new[] { "pot", "pan", "frying pan", "kettle", "cooker", "stove", "oven", "microwave", "blender", "mixer", "saucepan" }),
        ("Home & Kitchen", "Kitchen Appliances", new[] { "fridge", "freezer", "dispenser", "toaster", "juicer", "air conditioner" }),
        ("Home & Kitchen", "Home Decor", new[] { "curtain", "pillow", "picture frame", "photo frame", "vase", "rug", "mat", "clock", "mirror", "lamp", "wallpaper" }),
        ("Home & Kitchen", "Furniture", new[] { "chair", "table", "shelf", "bed", "wardrobe", "cabinet", "desk", "stool", "dresser", "dressing table", "coffee table" }),
        ("Home & Kitchen", "Cleaning Supplies", new[] { "detergent", "bleach", "omo", "ariel", "mop", "broom", "bucket", "sponge", "duster", "air freshener", "freshener", "disinfectant", "washing powder" }),
        ("Home & Kitchen", "Storage & Organization", new[] { "basket", "bin", "container", "rack", "hanger" }),
        ("Home & Kitchen", "Bedding", new[] { "bed sheet", "duvet", "blanket", "mattress", "pillow case" }),

        // Office & Stationery
        ("Office & Stationery", "Notebooks", new[] { "notebook", "journal", "diary", "exercise book", "jotter" }),
        ("Office & Stationery", "Pens & Writing Tools", new[] { "pen", "pencil", "marker", "highlighter pen", "eraser", "sharpener", "ruler" }),
        ("Office & Stationery", "Printing Supplies", new[] { "paper", "printer ink", "printer toner", "ink toner", "cartridge", "a4" }),
        ("Office & Stationery", "Office Equipment", new[] { "stapler", "punch", "tape", "scissors", "calculator", "file", "folder", "envelope" }),
        ("Office & Stationery", "School Supplies", new[] { "backpack", "school bag", "lunch box", "crayon", "color pencil" }),

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
        ("Tools & Hardware", "Electrical Supplies", new[] { "wire", "bulb", "switch", "breaker", "fuse", "led", "spirit level" }),
        ("Tools & Hardware", "Plumbing Supplies", new[] { "pipe", "tap", "valve", "water tank", "storage tank", "water pump", "fuel pump", "hose", "fitting" }),

        // Industrial & Bulk Supplies
        ("Industrial & Bulk Supplies", "Packaging Materials", new[] { "sack", "carton", "nylon", "polythene", "bubble wrap" }),
        ("Industrial & Bulk Supplies", "Wholesale Goods", new[] { "wholesale", "bulk" }),
        ("Industrial & Bulk Supplies", "Raw Materials", new[] { "rubber", "plastic", "chemical", "resin" }),
    };

    // Keywords that must match as whole words (word-boundary regex) to prevent
    // false positives from substring matches inside longer product names.
    private static readonly HashSet<string> WholeWordOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        // Original short words
        "pen", "bra", "ink", "tin", "tie", "cap", "mat", "tap", "rod", "saw",
        "mop", "bin", "fan", "led", "egg", "pie", "tea", "oil", "gel", "salt",
        "cream", "lace", "ring",
        // Jewelry metals — "gold ring" should match, "marigold" should not
        "gold", "silver", "platinum", "karat",
        // Food words that appear inside non-food products
        "water", "butter", "juice", "sauce", "coffee", "cocoa", "pepper", "mustard",
        // Beauty words that appear inside non-beauty products
        "powder", "primer", "blush", "serum",
        // Clothing words inside non-clothing products
        "dress", "skirt", "wrapper", "sandal", "boot",
        // Furniture/home words inside other products
        "table", "chair", "basket", "sheet", "block",
        // Electronics words
        "mouse", "switch",
        // Tools/hardware words
        "screw", "paint", "nail", "iron",
        // Other ambiguous words
        "glove", "paper", "rubber", "plastic", "nylon",
    };

    private static bool MatchesKeyword(string text, string keyword)
    {
        var trimmed = keyword.Trim();
        if (trimmed.Length <= 4 || WholeWordOnly.Contains(trimmed))
        {
            var pattern = @"(?<![a-z])" + Regex.Escape(trimmed) + @"(?![a-z])";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        }
        return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    public static (string? Category, string? Subcategory) Infer(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return (null, null);
        var padded = " " + productName.ToLowerInvariant() + " ";

        foreach (var (category, subcategory, keywords) in Map)
        {
            foreach (var keyword in keywords)
            {
                if (MatchesKeyword(padded, keyword))
                    return (category, subcategory);
            }
        }

        return (null, null);
    }
}

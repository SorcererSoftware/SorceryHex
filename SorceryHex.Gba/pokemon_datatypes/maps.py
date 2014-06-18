'''
static readonly Entry DataLayout = new Entry("map",
    new Entry("mapTileData", DataTypes.@pointer),
    new Entry("mapEventData",
        new Entry("personCount", DataTypes.@byte), new Entry("warpCount", DataTypes.@byte), new Entry("scriptCount", DataTypes.@byte), new Entry("signpostCount", DataTypes.@byte),
        new Entry("persons", "personCount",
            new Entry("?", DataTypes.@byte), new Entry("picture", DataTypes.@byte), new Entry("?", DataTypes.@byte), new Entry("?", DataTypes.@byte),
            new Entry("x", DataTypes.@short), new Entry("y", DataTypes.@short),
            new Entry("?", DataTypes.@byte), new Entry("movementType", DataTypes.@byte), new Entry("movement", DataTypes.@byte), new Entry("?", DataTypes.@byte),
            new Entry("isTrainer", DataTypes.@byte), new Entry("?", DataTypes.@byte), new Entry("viewRadius", DataTypes.@short),
            new Entry("script", DataTypes.@nullablepointer),
            new Entry("id", DataTypes.@short), new Entry("?", DataTypes.@byte), new Entry("?", DataTypes.@byte)
        ),
        new Entry("warps", "warpCount",
            new Entry("x", DataTypes.@short), new Entry("y", DataTypes.@short),
            new Entry("?", DataTypes.@byte), new Entry("warp", DataTypes.@byte), new Entry("map", DataTypes.@byte), new Entry("bank", DataTypes.@byte)
        ),
        new Entry("scripts", "scriptCount",
            new Entry("x", DataTypes.@short), new Entry("y", DataTypes.@short),
            new Entry("?", DataTypes.@short), new Entry("scriptVariable", DataTypes.@short),
            new Entry("scriptVariableValue", DataTypes.@short), new Entry("?", DataTypes.@short),
            new Entry("script", DataTypes.@nullablepointer)
        ),
        new Entry("signposts", "signpostCount",
            new Entry("x", DataTypes.@short), new Entry("y", DataTypes.@short),
            new Entry("talkingLevel", DataTypes.@byte), new Entry("signpostType", DataTypes.@byte), new Entry("?", DataTypes.@short),
            new Entry("?", DataTypes.@unknown4)
            // new Entry("dynamic", new DataType(4)) // *script || -itemID .hiddenID .amount || <missing>? //*/
        )
    ),
    new Entry("script", DataTypes.@nullablepointer),
    new Entry("connections", DataTypes.@nullablepointer,
        new Entry("count", DataTypes.@word),
        new Entry("data", DataTypes.@pointer)
    ),
    new Entry("song", DataTypes.@short), new Entry("map", DataTypes.@short),
    new Entry("label_id", DataTypes.@byte), new Entry("flash", DataTypes.@byte), new Entry("weather", DataTypes.@byte), new Entry("type", DataTypes.@byte),
    new Entry("_", DataTypes.@short), new Entry("labelToggle", DataTypes.@byte), new Entry("_", DataTypes.@byte)
);
'''

def mapLayout(b):
    b.ReadPointer("mapTileData", mapTileLayout)
    b.ReadPointer("mapEventData", eventLayout)
    b.ReadNullablePointer("script")
    b.ReadNullablePointer("connections")

    b.ReadShort("song")
    b.ReadShort("map")

    b.ReadByte("labelid")
    b.ReadByte("flash")
    b.ReadByte("weather")
    b.ReadByte("type")

    b.ReadShort("_")
    b.ReadByte("labelToggle")
    b.ReadByte("_")

def mapTileLayout(b):
    b.ReadWord("width")
    b.ReadWord("height")
    b.ReadPointer("borderTile")
    b.ReadPointer("tiles")
    b.ReadPointer("tileset1")
    b.ReadPointer("tileset2")
    b.ReadByte("borderWidth") # TODO only if FR / LG
    b.ReadByte("borderHeight")
    b.ReadShort("_")

def eventLayout(b):
    persons = b.ReadByte("personCount")
    warps = b.ReadByte("warpCount")
    scripts = b.ReadByte("scriptCount")
    signposts = b.ReadByte("signpostCount")
    b.ReadArray("persons", persons, personLayout)
    b.ReadArray("warps", warps, warpLayout)
    b.ReadArray("scripts", scripts, scriptLayout)
    b.ReadArray("signposts", signposts, signpostLayout)

def personLayout(b):
    b.ReadByte("?")
    b.ReadByte("picture")
    b.ReadShort("?")

    b.ReadShort("x")
    b.ReadShort("y")
    
    b.ReadByte("?")
    b.ReadByte("movementType")
    b.ReadByte("movement")
    b.ReadByte("?")

    b.ReadByte("isTrainer")
    b.ReadByte("?")
    b.ReadShort("viewRadius")
    
    b.ReadNullablePointer("script")
    b.ReadShort("id")
    b.ReadShort("?")

def warpLayout(b):
    b.ReadShort("x")
    b.ReadShort("y")
    b.ReadByte("?") # TODO add a link here
    b.ReadByte("warp")
    b.ReadByte("map")
    b.ReadByte("bank")

def scriptLayout(b):
    b.ReadShort("x")
    b.ReadShort("y")
    b.ReadShort("?")
    b.ReadShort("scriptVariable")
    b.ReadShort("scriptVariableValue")
    b.ReadShort("?")
    b.ReadNullablePointer("script")

def signpostLayout(b):
    b.ReadShort("x")
    b.ReadShort("y")
    b.ReadByte("talkingLevel")
    b.ReadByte("signpostType")
    b.ReadShort("?")
    b.ReadWord("?") # TODO script || -itemID .hiddenID .amount

matchingMapLayouts = types.FindMany(mapLayout)


# TODO do something with the matching map layouts. Find their root pointer or something.

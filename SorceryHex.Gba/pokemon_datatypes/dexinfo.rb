layout = "wwwwppwww"
layout = "wwwwpwww" if types.Version == "BPEE" # emerald

dexinfo = types.FindVariableArray layout, ->(b){
   b.String 12, "type"
   b.Short "height"
   b.Short "weight"
   b.StringPointer "description"
   b.StringPointer "description2" if types.Version != "BPEE" # emerald doesn't have 2 discription strings
   b.Unused 2
   b.Short "pokemonsize"
   b.Short "pokemonoffset"
   b.Short "trainersizer"
   b.Short "traineroffset"
   b.Unused 2
}

types.AddShortcut "dexinfo", dexinfo.destination
self.dexinfo = dexinfo.data

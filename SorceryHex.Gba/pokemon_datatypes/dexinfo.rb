datalayout = "wwwwppwww"
datalayout = "wwwwpwww" if types.Version == "BPEE" # emerald

layout = types.FindVariableArray datalayout, ->(b){
   b.String 12, "pokespecies"
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

# types.Label evolutions, ->(i) { return self.pokename[i].name }
types.AddShortcut "dexinfo", layout.destination
self.dexinfo = layout.data

_dexinfo = "wwwwppwww"
_dexinfo = "wwwwpwww" if types.Version == "BPEE" # emerald

self.dexinfo = types.FindVariableArray _dexinfo, ->(b){
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

types.Label dexinfo.data, ->(i) {
   k = -1
   for j in 0..dexorder.Length-1
      next if dexorder[j].index != i
      k = j+1
      break
   end
   return "" if k < 1
   return pokename[k].name
}

types.AddShortcut "dexinfo", dexinfo.destination
self.dexinfo = dexinfo.data
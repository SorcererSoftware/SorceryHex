datalocation = 0x42F6C
datalocation = 0x3F534 if types.Version == "AXVE" || types.Version == "AXPE"
datalocation = 0x6D140 if types.Version == "BPEE"

layout = types.ReadArray self.pokecount, datalocation, ->(b) {
   b.InlineArray "index", 5, ->(b) {
      t = b.Short "evotype"
      if t==0
         b.Unused 6
      else
         b.Short "param"
         b.Species pokename
         b.Unused 2
      end
   }
}

types.Label layout, ->(i) { return self.pokename[i].name }

types.AddShortcut "evolutions", layout[0].Location
self.evolutions = layout
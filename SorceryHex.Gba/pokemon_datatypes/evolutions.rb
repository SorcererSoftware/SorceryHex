types.WaitFor "0pokename"

_evolutions = 0x42F6C
_evolutions = 0x3F534 if types.Version == "AXVE" || types.Version == "AXPE"
_evolutions = 0x6D140 if types.Version == "BPEE"

self.evolutions = types.ReadArray self.pokecount, _evolutions, ->(b) {
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

types.Label evolutions, ->(i) { return pokename[i].name }

types.AddShortcut "evolutions", evolutions[0].Location
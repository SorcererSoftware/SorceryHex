datalocation = 0x42F6C
if types.Version == "AXVE" || types.Version == "AXPE"
   datalocation = 0x3F534 # Ruby / Sapphire
end
if types.Version == "BPEE"
   datalocation = 0x6D140 # Emerald
end

layout = types.ReadArray 412, datalocation, ->(b) {
   b.InlineArray "index", 5, ->(b) {
      t = b.Short "evotype"
      if t==0
         b.Unused 6
      else
         b.Short "param"
         b.Species
         b.Unused 2
      end
   }
}

types.Label layout, ->(i) { return self.pokename[i].name }
types.AddShortcut "evolutions", layout.destinationof(0)
self.evolutions = layout
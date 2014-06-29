datalocation = 0x144
datalocation = 0xFA58 if types.Version == "AXVE" || types.Version == "AXPE"

layout = types.ReadArray self.pokecount, datalocation, ->(b) {
   b.String 11, "name"
}

types.AddShortcut "pokename", layout.destinationof(0)
self.pokename = layout
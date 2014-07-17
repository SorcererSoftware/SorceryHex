_poketype = 0x309C8
_poketype = 0x121B60 if types.Version == "AXVE" || types.Version == "AXPE"
_poketype = 0x59C24 if types.Version == "BPEE"

self.poketype = types.ReadArray 0x12, _poketype, ->(b) {
   b.String 7, "name"
}

types.AddShortcut "poketype", poketype[0].Location
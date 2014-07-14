datalocation = 0x309C8
datalocation = 0x121B60 if types.Version == "AXVE" || types.Version == "AXPE"
datalocation = 0x59C24 if types.Version == "BPEE"

layout = types.ReadArray 0x12, datalocation, ->(b) {
   b.String 7, "name"
}

types.AddShortcut "poketype", layout[0].Location
self.poketype = layout
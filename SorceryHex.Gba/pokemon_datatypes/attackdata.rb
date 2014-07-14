datalocation = 0x1CC
datalocation = 0xCA54 if types.Version == "AXVE" || types.Version == "AXPE"

layout = types.ReadArray self.attackcount, datalocation, ->(b) {
   b.Byte "effect"
   b.Byte "power"
   b.ByteEnum "type", poketype
   b.Byte "accuracy"
   b.Byte "pp"
   b.Byte "unknown1"
   b.Short "target"
   b.Word "unknown2"
}

types.Label layout, ->(i) { return self.attackname[i].name }
types.AddShortcut "attackdata", layout[0].Location
self.attackdata = layout
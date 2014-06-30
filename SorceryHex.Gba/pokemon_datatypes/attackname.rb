datalocation = 0x148
datalocation = 0x2E18C if types.Version == "AXVE" || types.Version == "AXPE"

layout = types.ReadArray self.attackcount, datalocation, ->(b) {
   b.String 13, "name"
}

types.AddShortcut "attackname", layout[0].Location
self.attackname = layout
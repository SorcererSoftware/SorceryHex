datalocation = 0x1C0
datalocation = 0x9FE64 if types.Version == "AXVE" || types.Version == "AXPE"

layout = types.ReadArray self.abilitycount, datalocation, ->(b) {
   b.String 13, "name"
}
types.AddShortcut "abilityname", layout[0].Location
self.abilityname = layout

datalocation += 4
layout = types.ReadArray self.abilitycount, datalocation, ->(b) {
   b.StringPointer "description"
}
types.AddShortcut "abilitydescription", layout[0].Location
self.abilitydescription = layout
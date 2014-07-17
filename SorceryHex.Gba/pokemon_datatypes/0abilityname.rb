types.WaitFor "_setup"

_abilityname = 0x1C0
_abilityname = 0x9FE64 if types.Version == "AXVE" || types.Version == "AXPE"

self.abilityname = types.ReadArray abilitycount, _abilityname, ->(b) {
   b.String 13, "name"
}
types.AddShortcut "abilityname", abilityname[0].Location

_abilitydescription = _abilityname + 4
self.abilitydescription = types.ReadArray abilitycount, _abilitydescription, ->(b) {
   b.StringPointer "description"
}
types.AddShortcut "abilitydescription", abilitydescription[0].Location
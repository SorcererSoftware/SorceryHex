types.WaitFor "0poketype"
types.WaitFor "0abilityname"

_stats = 0x1BC
_stats = 0x10B64 if types.Version == "AXVE" || types.Version == "AXPE"

self.stats = types.ReadArray pokecount, _stats, ->(b) {
   b.ByteNum "health"
   b.ByteNum "attack"
   b.ByteNum "defense"
   b.ByteNum "speed"
   b.ByteNum "spattack"
   b.ByteNum "spdefense"
   b.ByteEnum "type1", poketype
   b.ByteEnum "type2", poketype
   b.ByteNum "catchrate"
   b.ByteNum "exp"
   b.Unused 3 # evs
   b.Byte "item1"
   b.Byte "item2"
   b.Unused 1
   b.ByteNum "genderratio"
   b.ByteNum "hatchspeed"
   b.ByteNum "basefriendship"
   b.ByteNum "levelup"
   b.Byte "egggroup1"
   b.Byte "egggroup2"
   b.ByteEnum "ability1", abilityname
   b.ByteEnum "ability2", abilityname
   b.ByteNum "runrate"
   b.ByteNum "color"
   b.Unused 2
}

types.Label stats, ->(i) { return pokename[i].name }

types.AddShortcut "stats", stats[0].Location
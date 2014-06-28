statlocation = 0x1BC
if types.Version == "AXVE" || types.Version == "AXPE"
   statlocation = 0x10B64
end

statsArray = types.ReadArray "wwwwwww", 411, statlocation, ->(b) {
   b.Byte "health"
   b.Byte "attack"
   b.Byte "defense"
   b.Byte "speed"
   b.Byte "spattack"
   b.Byte "spdefense"
   b.Byte "type1"
   b.Byte "type2"
   b.Byte "catchrate"
   b.Byte "exp"
   b.Unused 3 # evs
   b.Byte "item1"
   b.Byte "item2"
   b.Unused 1
   b.Byte "genderratio"
   b.Byte "hatchspeed"
   b.Byte "basefriendship"
   b.Byte "levelup"
   b.Byte "egggroup1"
   b.Byte "egggroup2"
   b.Byte "ability1"
   b.Byte "ability2"
   b.Byte "runrate"
   b.Byte "color"
   b.Unused 2
}

self.stats = statsArray.destinationof(0)
self.statsdata = statsArray
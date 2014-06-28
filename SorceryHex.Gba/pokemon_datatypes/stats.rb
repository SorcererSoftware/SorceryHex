statlocation = 0x1BC
if types.Version == "AXVE" || types.Version == "AXPE"
   statlocation = 0x10B64
end

statsArray = types.ReadArray 412, statlocation, ->(b) {
   b.ByteNum "health"
   b.ByteNum "attack"
   b.ByteNum "defense"
   b.ByteNum "speed"
   b.ByteNum "spattack"
   b.ByteNum "spdefense"
   b.Byte "type1"
   b.Byte "type2"
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
   b.Byte "ability1"
   b.Byte "ability2"
   b.ByteNum "runrate"
   b.ByteNum "color"
   b.Unused 2
}

types.Label statsArray, ->(i) { return self.pokenamedata[i].name }
types.AddShortcut "stats", statsArray.destinationof(0)
self.stats = statsArray
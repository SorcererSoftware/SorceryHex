namelocation = 0x144
if types.Version == "AXVE" || types.Version == "AXPE"
   namelocation = 0xFA58
end

nameArray = types.ReadArray 412, namelocation, ->(b) {
   b.String 11, "name"
}

types.AddShortcut "pokename", nameArray.destinationof(0)
self.pokename = nameArray
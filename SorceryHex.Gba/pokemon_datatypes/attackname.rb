namelocation = 0x148
if types.Version == "AXVE" || types.Version == "AXPE"
   namelocation = 0x2E18C
end

nameArray = types.ReadArray 355, namelocation, ->(b) {
   b.String 13, "name"
}

types.AddShortcut "attackname", nameArray.destinationof(0)
self.attackname = nameArray
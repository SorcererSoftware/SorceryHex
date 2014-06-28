attacknamelocation = 0x148
if types.Version == "AXVE" || types.Version == "AXPE"
   pokenamelocation = 0x2E18C
end

nameArray = types.ReadArray 355, pokenamelocation, ->(b) {
   b.String 13, "name"
}

self.attackname = nameArray.destinationof(0)
self.attacknamedata = nameArray
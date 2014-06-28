pokenamelocation = 0x144
if types.Version == "AXVE" || types.Version == "AXPE"
   pokenamelocation = 0xFA58
end

nameArray = types.ReadArray 412, pokenamelocation, ->(b) {
   b.String 11, "name"
}

self.pokename = nameArray.destinationof(0)
self.pokenamedata = nameArray
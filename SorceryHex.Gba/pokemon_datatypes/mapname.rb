_mapname = 0xFB550
_mapname = 0xC0C94 if types.Version == "BPRE" # firered
_mapname = 0xC0C68 if types.Version == "BPGE" # leafgreen
_mapname = 0x123B44 if types.Version == "BPEE" # emerald

if types.Version == "BPRE" || types.Version == "BPGE"
   self.mapname = types.ReadArray 109, _mapname, ->(b) {
      b.StringPointer "name"
   }
else
   _mapnamecount = 88
   _mapnamecount = 213 if types.Version == "BPEE"
   self.mapname = types.ReadArray _mapnamecount, _mapname, ->(b) {
      b.Unused 4
      b.StringPointer "name"
   }
end

types.AddShortcut "mapname", mapname[0].Location
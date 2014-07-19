_mapname = 0xC0C94
# TODO test other versions besides FireRed

self.mapname = types.ReadArray 109, _mapname, ->(b) {
   b.StringPointer "name"
}

types.AddShortcut "mapname", mapname[0].Location
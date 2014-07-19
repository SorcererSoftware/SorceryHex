types.WaitFor "0pokename"

encounter = ->(b){
   low = b.ByteNum "low"
   high = b.ByteNum "high"
   b.Assert (low<=high && high<=100), "pokemon levels range from 1-100"
   b.Species pokename
}

self.wild = types.FindVariableArray 0xFF, "wpppp", ->(b){
   b.Link 2, "bankmap", ->(b){
      bank = b.Byte "bank"
      map  = b.Byte "map"
      return maps[bank][map].Location
   }
   b.Unused 2
   b.NullablePointer "grass", ->(b){
      b.Word "rate"
      b.Array "encounters", 12, encounter
   }
   b.NullablePointer "surf", ->(b){
      b.Word "rate"
      b.Array "encounters", 5, encounter
   }
   b.NullablePointer "tree", ->(b){
      b.Word "rate"
      b.Array "encounters", 5, encounter
   }
   b.NullablePointer "fish", ->(b){
      b.Word "rate"
      b.Array "encounters", 10, encounter
   }
}

types.Label wild.data, ->(i) {
   bank = app.data[wild[i].Location]
   map  = app.data[wild[i].Location + 1]
   return mapname[maps[bank][map].labelid - 0x58].name
}

types.AddShortcut "wild", wild.destination
self.wild = wild.data
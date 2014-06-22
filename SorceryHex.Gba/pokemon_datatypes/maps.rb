maps = types.FindMany "ppppwww", ->(b){
   b.Pointer "mapTileData", ->(b){
      b.Word "width" 
      b.Word "height" 
      b.Pointer "borderTile" 
      b.Pointer "tiles" 
      b.Pointer "tileset1" 
      b.Pointer "tileset2" 
      b.Byte "borderWidth" # TODO only if FR / LG
      b.Byte "borderHeight"
      b.Short "_"
   }
   b.Pointer "mapEventData", ->(b){
      persons = b.Byte "personCount"
      warps = b.Byte "warpCount"
      scripts = b.Byte "scriptCount"
      signposts = b.Byte "signpostCount"
      b.Array "persons", persons, ->(b){
         b.Byte "id"; b.Byte "picture"; b.Short "?"
         b.Short "x"; b.Short "y"
         b.Byte "?"; b.Byte "movementType"; b.Byte "movement"; b.Byte "?"
         b.Byte "isTrainer"; b.Byte "?"; b.Short "viewRadius"
         b.NullablePointer "script"
         b.Short "id"; b.Short "?"
      }
      b.Array "warps", warps, ->(b){
         b.Short "x"; b.Short "y"
         b.Byte "?"; b.Byte "warp"; b.Byte "map"; b.Byte "bank" # TODO link
      }
      b.Array "scripts", scripts, ->(b){
         b.Short "x"; b.Short "y"
         b.Short "?"; b.Short "scriptVariable"
         b.Short "scriptVariableValue"; b.Short "?"
         b.NullablePointer "script"
      }
      b.Array "signposts", signposts, ->(b){
         b.Short "x"; b.Short "y"
         b.Byte "talkingLevel"; b.Byte "signpostType"; b.Short "?"
         b.Word "?" # TODO script || -itemID .hiddenID .amount
      }
   }
   b.NullablePointer "script"
   b.NullablePointer "connections", ->(b){
      b.Word "count"
      b.NullablePointer "data"
   }
   b.Short "song"; b.Short "map"
   b.Byte "labelid"; b.Byte "flash"; b.Byte "weather"; b.Byte "type"
   b.Short "_"; b.Byte "labelToggle"; b.Byte "_"
}

banks = types.FollowPointersUp maps
_map = types.FollowPointersUp banks
mapdata = _map[0].data
mapbank = _map[0].destination


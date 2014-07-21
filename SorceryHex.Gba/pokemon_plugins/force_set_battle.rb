# Forces trainer battles to always be in the "set" style.
# The player will be unable to change
# pokemon between opponent pokemon.

# the current version of the script only works for fr/lg
if types.Version != "BPRE" && types.Version != "BPGE"
   app.status "force_set_battle only supports FireRed and LeafGreen"
   return
end

results = app.find "87 1D 08 2B 04 D0 3D 02"
app.data[results[0] + 3] = 0x28
app.data[results[0] + 4] = 0x92
app.data[results[0] + 5] = 0x87
app.data[results[0] + 6] = 0x1D
app.data[results[0] + 7] = 0x08
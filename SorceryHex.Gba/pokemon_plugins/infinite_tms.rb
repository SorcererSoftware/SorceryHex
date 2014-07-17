# makes it possible to use a tm any number of times

# emerald is different
if types.Version == "BPEE"
   app.data[0x1B6EE0] = 0x90
   return
end

# all instances of the hex string refer to changing tm amounts
results = app.find '88 A9 20 40 00 81 42 03 D8 08 1C 01 21'

# replace the second byte (A9) with 90.
for i in results
   app.data[i+1] = 0x90
end
capsA = 0xBB # pcs for 'A'
lowerA = 0xD5# pcs for 'a'
tick = 0xB4  # pcs for '''
male = 0xB5  # pcs for '♂'
female = 0xB6# pcs for '♀'
dot = 0xAD   # pcs for '.'
poke= 0x1B   # pcs for 'é'
space = 0x00 # pcs for ' '

wordLength = 0
list = Array.new

for i in 0..(app.data.Length-1)
   app.status("#{i}...") if i%0x10000 == 0
   isPartOfCapsWord = app.data[i]>=capsA && app.data[i]<capsA+26
   next if !isPartOfCapsWord && wordLength==0
   isPartOfCapsWord = true if app.data[i]==tick   # farfetch'd, ho'oh
   isPartOfCapsWord = true if app.data[i]==dot    # mr. mime
   isPartOfCapsWord = true if app.data[i]==space  # mr. mime
   isPartOfCapsWord = true if app.data[i]==male   # nidoran
   isPartOfCapsWord = true if app.data[i]==female # nidoran
   isPartOfCapsWord = true if app.data[i]==poke   # pokecenter
   if isPartOfCapsWord
      wordLength += 1
      next
   end

   # required it to end with 0xFF decreases the likelyhood of false positives
   # but it also causes us to miss several known true positives
   if wordLength<3 || app.data[i]!=0xFF
      wordLength = 0
      next
   end

   # impl 1: return a list of all the locations for user review
#   list << i-wordLength+1
#   wordLength = 0

   # impl 2: return a list of all words to be changed
#   start = i-wordLength
#   word = ""
#   for j in 0..(wordLength-1)
#      if capsA<=app.data[start+j] && app.data[start+j]<capsA+26
#         c = ('A'.ord-capsA+app.data[start+j]).chr
#         word = word + c
#      end
#   end
#   list << word
#   wordLength = 0

   # impl 3: change each location to be lower-case
   start = i-wordLength
   word = ""
   for j in 1..(wordLength-1)
      if capsA<=app.data[start+j] && app.data[start+j]<capsA+26
         app.data[start+j] += lowerA-capsA
         c = ('A'.ord-capsA+app.data[start+j]).chr
         word = word + c
      end
   end
   app.status word
   wordLength = 0
end

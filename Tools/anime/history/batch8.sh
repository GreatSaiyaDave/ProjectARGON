S=/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad
cd $S
./venv/bin/python anime/harvest.py "Mechanical Mayhem, Part 1" 60 1150 1 $S/anime/ep/tristan_mm1 130 2>&1 | tail -1
./venv/bin/python anime/harvest.py "Mechanical Mayhem, Part 2" 60 1150 1 $S/anime/ep/tristan_mm2 130 2>&1 | tail -1
mkdir -p $S/anime/keith_scan; f=$(ls "/home/greatsaiyadave/Downloads/YouTube Downloader" | grep "Keith's Machinations, Part 2" | head -1)
ffmpeg -nostdin -v error -y -ss 200 -i "/home/greatsaiyadave/Downloads/YouTube Downloader/$f" -t 950 -vf "fps=0.5,scale=320:-1" "$S/anime/keith_scan/k_%03d.png"
./venv/bin/python - <<'PY'
import glob
from PIL import Image, ImageDraw
S='/tmp/claude-1000/-home-greatsaiyadave-DMWRLDZUnityProject-ProjectARGON/a69659a5-e142-4e17-b0c8-a9f5b9d69935/scratchpad/anime/keith_scan/'
fs=sorted(glob.glob(S+'k_*.png')); w,h=Image.open(fs[0]).size; cols=12
for part in range(0,len(fs),240):
    sub=fs[part:part+240]; rows=(len(sub)+cols-1)//cols
    sheet=Image.new('RGB',(cols*w, rows*(h+12)),(20,20,25)); d=ImageDraw.Draw(sheet)
    for i,f in enumerate(sub):
        x=(i%cols)*w; y=(i//cols)*(h+12); sheet.paste(Image.open(f),(x,y)); d.text((x+2,y+h),f'{200+(part+i)*2}s',fill=(240,240,240))
    sheet.save(S+f'sheet_{part//240}.png'); print(sheet.size)
PY
echo BATCH8_DONE

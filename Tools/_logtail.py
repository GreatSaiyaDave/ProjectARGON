p="Logs/Editor.log"
f=open(p,errors="replace")
f.seek(0,2)
n=f.tell()
f.seek(max(0,n-250000))
s=f.read()
print(s[-200000:])

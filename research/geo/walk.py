import pickle, numpy as np, sys, collections
data = pickle.load(open(sys.argv[1],'rb'))
CS = 0.5
floors = collections.defaultdict(list)   # cell -> list of floor y
walls = collections.defaultdict(list)    # cell -> list of (ymin,ymax,name)
def cells_in_tri(a,b,c):
    xs=[a[0],b[0],c[0]]; zs=[a[2],b[2],c[2]]
    for ix in range(int(np.floor(min(xs)/CS)), int(np.floor(max(xs)/CS))+1):
        for iz in range(int(np.floor(min(zs)/CS)), int(np.floor(max(zs)/CS))+1):
            yield ix,iz
def bary(p,a,b,c):
    v0=(c[0]-a[0],c[2]-a[2]); v1=(b[0]-a[0],b[2]-a[2]); v2=(p[0]-a[0],p[1]-a[2])
    d00=v0[0]*v0[0]+v0[1]*v0[1]; d01=v0[0]*v1[0]+v0[1]*v1[1]; d11=v1[0]*v1[0]+v1[1]*v1[1]
    d20=v2[0]*v0[0]+v2[1]*v0[1]; d21=v2[0]*v1[0]+v2[1]*v1[1]
    den=d00*d11-d01*d01
    if abs(den)<1e-12: return None
    u=(d11*d20-d01*d21)/den; v=(d00*d21-d01*d20)/den
    return u,v
for name,(V,F) in data.items():
    for f in F:
        a,b,c = V[f[0]],V[f[1]],V[f[2]]
        n = np.cross(b-a,c-a); ln=np.linalg.norm(n)
        if ln<1e-9: continue
        n/=ln
        if abs(n[1])>=0.6:
            for ix,iz in cells_in_tri(a,b,c):
                p=((ix+0.5)*CS,(iz+0.5)*CS)
                r=bary(p,a,b,c)
                if r and r[0]>=-0.02 and r[1]>=-0.02 and r[0]+r[1]<=1.02:
                    y=a[1]+r[0]*(c[1]-a[1])+r[1]*(b[1]-a[1])
                    floors[(ix,iz)].append(y)
        else:
            # sample along the triangle densely
            for s in np.linspace(0,1,12):
                for t in np.linspace(0,1-s,max(2,int(12*(1-s)))):
                    p=a+s*(b-a)+t*(c-a)
                    walls[(int(np.floor(p[0]/CS)),int(np.floor(p[2]/CS)))].append((p[1],name))
def blocked(cell,y):
    for wy,_ in walls.get(cell,[]):
        if y+0.35 < wy < y+1.5: return True
    return False
def floor_near(cell,y):
    best=None
    for fy in floors.get(cell,[]):
        if fy<=y+0.6 and fy>=y-1.0 and (best is None or abs(fy-y)<abs(best-y)): best=fy
    return best
def flood(start):
    sc=(int(np.floor(start[0]/CS)),int(np.floor(start[2]/CS)))
    seen={sc:start[1]}; q=collections.deque([sc])
    while q:
        c=q.popleft(); y=seen[c]
        for dx,dz in ((1,0),(-1,0),(0,1),(0,-1)):
            n=(c[0]+dx,c[1]+dz)
            if n in seen: continue
            fy=floor_near(n,y)
            if fy is None or blocked(n,fy): continue
            seen[n]=fy; q.append(n)
    return seen
start=tuple(float(v) for v in sys.argv[2].split(','))
seen=flood(start)
print('reached',len(seen))
for t in sys.argv[3:]:
    p=[float(v) for v in t.split(',')]
    c=(int(np.floor(p[0]/CS)),int(np.floor(p[2]/CS)))
    near=[k for k in seen if abs(k[0]-c[0])<=4 and abs(k[1]-c[1])<=4]
    print(t,'reachable' if near else 'NOT reachable')
# ascii map, 1 char = 1 m
xs=[k[0] for k in floors]; zs=[k[1] for k in floors]
rows=[]
for iz in range(max(zs),min(zs)-1,-2):
    row=''
    for ix in range(min(xs),max(xs)+1,2):
        c=(ix,iz)
        if c in seen: ch='.'
        elif c in floors: ch='#' if blocked(c,max(floors[c])) else ','
        else: ch=' '
        row+=ch
    rows.append('%5.0f %s'%(iz*CS,row))
open('map.txt','w').write('\n'.join(rows))

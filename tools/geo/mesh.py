import UnityPy, sys, numpy as np, pickle
env = UnityPy.load(sys.argv[1])
out = {}
def world_matrix(tr):
    m = np.eye(4)
    while tr is not None:
        p = tr.m_LocalPosition; r = tr.m_LocalRotation; s = tr.m_LocalScale
        x,y,z,w = r.x,r.y,r.z,r.w
        R = np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
        L = np.eye(4); L[:3,:3] = R @ np.diag([s.x,s.y,s.z]); L[:3,3] = [p.x,p.y,p.z]
        m = L @ m
        tr = tr.m_Father.read() if tr.m_Father and tr.m_Father.path_id else None
    return m
for o in env.objects:
    if o.type.name != "MeshCollider": continue
    c = o.read(); go = c.m_GameObject.read()
    mesh = c.m_Mesh.read()
    tr = None
    for entry in go.m_Component:
        ptr = entry[1] if isinstance(entry, tuple) else (entry.component if hasattr(entry,'component') else entry)
        if ptr.type.name == "Transform": tr = ptr.read()
    M = world_matrix(tr)
    # export via obj text
    obj = mesh.export()
    V=[];F=[]
    for line in obj.splitlines():
        if line.startswith('v '): V.append([float(a) for a in line.split()[1:4]])
        elif line.startswith('f '):
            F.append([int(t.split('/')[0])-1 for t in line.split()[1:4]])
    V=np.array(V); V[:,0]*=-1  # UnityPy obj export flips x
    Vw=(M@np.c_[V,np.ones(len(V))].T).T[:,:3]
    out[go.m_Name]=(Vw,np.array(F))
    print(go.m_Name, len(V), len(F), Vw.min(0).round(1), Vw.max(0).round(1))
pickle.dump(out, open(sys.argv[2],'wb'))

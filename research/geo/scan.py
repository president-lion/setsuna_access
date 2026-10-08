import UnityPy, sys, collections
env = UnityPy.load(sys.argv[1])
types = collections.Counter(o.type.name for o in env.objects)
print(types.most_common(30))
names = collections.Counter()
for o in env.objects:
    if o.type.name in ("MeshCollider","BoxCollider"):
        d = o.read()
        go = d.m_GameObject.read()
        names[(o.type.name, go.m_Name, go.m_Layer)] += 1
for k,v in sorted(names.items(), key=lambda x:-x[1])[:60]: print(k,v)

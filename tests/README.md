These native integration tests require a working graphics context:
`dotnet test tests/ModelInstanceTests.csproj -c Release`.

Fixtures are self-contained and authored for these tests:
- triangle.obj: static, unindexed triangle.
- skinned.gltf: indexed triangle weighted to one joint under an Armature node, with a one-second Run translation animation. Its buffer is embedded as base64. Raylib 6.0's animation loader requires a parent for the root joint.
- empty.gltf: parseable glTF with no geometry, testing rejection and repeated cleanup.

Both shared catalogue loads and independent instances must accept static and skinned geometry. The animation test also checks native clip compatibility and actual vertex movement.

Raylib 6.0 IsModelValid rejects CPU-skinned meshes when boneIndices is non-null but vboId[7] is zero (and similarly boneWeights/vboId[8]). GPU skinning buffers are optional; these fields do not establish whether model geometry is usable. AssetManager instead checks mesh/material arrays and counts, vertex/triangle data, material mappings and index bounds. Failed loads still call UnloadModel before throwing.

Package version 0.1.4 distinguishes this validation fix from the already-packed 0.1.3 described in the bug report, avoiding different binaries under the same NuGet package identity.

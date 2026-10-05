using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using WildRenderingSharp.Cloth.Model;
using WildRenderingSharp.Cloth.Model.Animation;
using WildRenderingSharp.Cloth.Model.Collidables;
using WildRenderingSharp.Cloth.Model.Constraints;
using WildRenderingSharp.Cloth.Model.Operators;

namespace WildRenderingSharp.Cloth.Format;

public sealed class HavokDeserializer
{
    private readonly TagFile _tagFile;
    private readonly byte[] _data;

    public HavokDeserializer(TagFile tagFile)
    {
        _tagFile = tagFile;
        _data = tagFile.Data;
    }

    public static (HclClothContainer ClothContainer, HkaAnimationContainer? AnimationContainer) Deserialize(TagFile tagFile)
    {
        var deserializer = new HavokDeserializer(tagFile);
        return deserializer.DeserializeRoot();
    }

    private (HclClothContainer, HkaAnimationContainer?) DeserializeRoot()
    {
        // Root container is at offset 0:
        // hkArray<NamedVariant> m_namedVariants at offset 0 (ptr at 0, count at 8)
        var (variantsOffset, variantCount) = ReadArrayHeader(0);

        HclClothContainer? clothContainer = null;
        HkaAnimationContainer? animationContainer = null;

        for (int i = 0; i < variantCount; i++)
        {
            int entryOffset = variantsOffset + i * 24;
            string name = ReadStringPointer(entryOffset);
            string className = ReadStringPointer(entryOffset + 8);
            int variantPtr = ReadPointer(entryOffset + 16);

            if (className == "hclClothContainer" && variantPtr > 0)
            {
                clothContainer = DeserializeClothContainer(variantPtr);
            }
            else if (className == "hkaAnimationContainer" && variantPtr > 0)
            {
                animationContainer = DeserializeAnimationContainer(variantPtr);
            }
        }

        clothContainer ??= new HclClothContainer();
        return (clothContainer, animationContainer);
    }

    private HclClothContainer DeserializeClothContainer(int offset)
    {
        var container = new HclClothContainer();

        // hkReferencedObject base is 24 bytes (0x18)
        // m_collidables is at offset + 24 (0x18)
        var (collidablesOffset, collidableCount) = ReadArrayHeader(offset + 24);
        for (int i = 0; i < collidableCount; i++)
        {
            int colPtr = ReadPointer(collidablesOffset + i * 8);
            if (colPtr > 0)
            {
                container.Collidables.Add(DeserializeCollidable(colPtr));
            }
        }

        // m_clothDatas is at offset + 40 (0x28)
        var (clothDatasOffset, clothDataCount) = ReadArrayHeader(offset + 40);
        for (int i = 0; i < clothDataCount; i++)
        {
            int clothPtr = ReadPointer(clothDatasOffset + i * 8);
            if (clothPtr > 0)
            {
                container.ClothDatas.Add(DeserializeClothData(clothPtr));
            }
        }

        return container;
    }

    private HclCollidable DeserializeCollidable(int offset)
    {
        // offset + 32: hkTransform m_transform (64 bytes)
        Matrix4x4 transform = ReadTransform(offset + 32);

        // offset + 136: hkRefPtr<hclShape> m_shape
        int shapePtr = ReadPointer(offset + 136);
        HclShape? shape = shapePtr > 0 ? DeserializeShape(shapePtr) : null;

        // offset + 144: hkStringPtr m_name
        string name = ReadStringPointer(offset + 144);

        return new HclCollidable
        {
            Name = name,
            Transform = transform,
            Shape = shape,
            // hclCollidable v4 fields following m_name.
            PinchDetectionRadius = ReadFloat(offset + 152),
            PinchDetectionPriority = unchecked((sbyte)_data[offset + 156]),
            PinchDetectionEnabled = ReadBool(offset + 157),
            VirtualCollisionPointCollisionEnabled = ReadBool(offset + 158),
            Enabled = ReadBool(offset + 159)
        };
    }

    private HclShape? DeserializeShape(int offset)
    {
        // offset + 24: int32 m_type
        int type = ReadInt32(offset + 24);
        switch (type)
        {
            case 0: // Capsule
            {
                Vector3 start = ReadVector3(offset + 32);
                Vector3 end = ReadVector3(offset + 48);
                Vector3 dir = ReadVector3(offset + 64);
                float radius = ReadFloat(offset + 80);
                float capLenSqrdInv = ReadFloat(offset + 84);
                return new HclCapsuleShape
                {
                    Start = start,
                    End = end,
                    Direction = dir,
                    Radius = radius,
                    CapLenSqrdInv = capLenSqrdInv
                };
            }
            case 1: // Sphere
            {
                Vector3 center = ReadVector3(offset + 32);
                float radius = ReadFloat(offset + 44);
                return new HclSphereShape
                {
                    Center = center,
                    Radius = radius
                };
            }
            case 2: // Tapered Capsule
            {
                Vector3 small = ReadVector3(offset + 32);
                Vector3 big = ReadVector3(offset + 48);
                float smallRadius = ReadFloat(offset + 144);
                float bigRadius = ReadFloat(offset + 148);
                return new HclTaperedCapsuleShape
                {
                    Small = small,
                    Big = big,
                    SmallRadius = smallRadius,
                    BigRadius = bigRadius
                };
            }
            case 3: // Plane
            {
                Vector4 plane = ReadVector4(offset + 32);
                return new HclPlaneShape
                {
                    PlaneEquation = plane
                };
            }
            default:
                return null;
        }
    }

    private HclClothData DeserializeClothData(int offset)
    {
        var cloth = new HclClothData();

        // offset + 24: m_name
        cloth.Name = ReadStringPointer(offset + 24);

        // offset + 32: m_simClothDatas
        var (simDatasOffset, simDataCount) = ReadArrayHeader(offset + 32);
        for (int i = 0; i < simDataCount; i++)
        {
            int simPtr = ReadPointer(simDatasOffset + i * 8);
            if (simPtr > 0)
            {
                cloth.SimClothDatas.Add(DeserializeSimClothData(simPtr));
            }
        }

        // offset + 48: m_bufferDefinitions
        var (bufDefsOffset, bufDefCount) = ReadArrayHeader(offset + 48);
        for (int i = 0; i < bufDefCount; i++)
        {
            int bufPtr = ReadPointer(bufDefsOffset + i * 8);
            if (bufPtr > 0)
            {
                cloth.BufferDefinitions.Add(DeserializeBufferDefinition(bufPtr));
            }
        }

        // offset + 64: m_transformSetDefinitions
        var (tsDefsOffset, tsDefCount) = ReadArrayHeader(offset + 64);
        for (int i = 0; i < tsDefCount; i++)
        {
            int tsPtr = ReadPointer(tsDefsOffset + i * 8);
            if (tsPtr > 0)
            {
                cloth.TransformSetDefinitions.Add(DeserializeTransformSetDefinition(tsPtr));
            }
        }

        // offset + 80: m_operators
        var (opsOffset, opCount) = ReadArrayHeader(offset + 80);
        for (int i = 0; i < opCount; i++)
        {
            int opPtr = ReadPointer(opsOffset + i * 8);
            if (opPtr > 0)
            {
                var op = DeserializeOperator(opPtr);
                if (op != null)
                {
                    cloth.Operators.Add(op);
                }
            }
        }

        // offset + 148: m_targetPlatform
        cloth.TargetPlatform = ReadUInt32(offset + 148);

        return cloth;
    }

    private HclSimClothData DeserializeSimClothData(int offset)
    {
        var simCloth = new HclSimClothData();

        // offset + 24: m_name
        simCloth.Name = ReadStringPointer(offset + 24);

        // offset + 32: m_simulationInfo
        simCloth.Gravity = ReadVector3(offset + 32);
        simCloth.GlobalDamping = ReadFloat(offset + 48);

        // offset + 64: m_particleDatas
        var (particlesOffset, particleCount) = ReadArrayHeader(offset + 64);
        var particles = new HclParticleData[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            int pOff = particlesOffset + i * 16;
            float mass = ReadFloat(pOff);
            float invMass = ReadFloat(pOff + 4);
            float radius = ReadFloat(pOff + 8);
            float friction = ReadFloat(pOff + 12);
            particles[i] = new HclParticleData(mass, invMass, radius, friction);
        }
        simCloth.Particles = particles;

        // offset + 80: m_fixedParticles
        var (fixedOffset, fixedCount) = ReadArrayHeader(offset + 80);
        var fixedParticles = new ushort[fixedCount];
        for (int i = 0; i < fixedCount; i++)
        {
            fixedParticles[i] = ReadUInt16(fixedOffset + i * 2);
        }
        simCloth.FixedParticles = fixedParticles;

        // offset + 120: m_simClothPoses
        var (posesOffset, poseCount) = ReadArrayHeader(offset + 120);
        var poses = new List<HclSimClothPose>();
        for (int i = 0; i < poseCount; i++)
        {
            int posePtr = ReadPointer(posesOffset + i * 8);
            if (posePtr > 0)
            {
                string poseName = ReadStringPointer(posePtr + 24);
                var (posOffset, posCount) = ReadArrayHeader(posePtr + 32);
                var posArray = new Vector3[posCount];
                for (int p = 0; p < posCount; p++)
                {
                    posArray[p] = ReadVector3(posOffset + p * 16);
                }
                poses.Add(new HclSimClothPose
                {
                    Name = poseName,
                    Positions = posArray
                });
            }
        }
        simCloth.Poses = poses.ToArray();

        // offset + 136: m_staticConstraintSets
        var (constraintsOffset, constraintCount) = ReadArrayHeader(offset + 136);
        var constraints = new List<HclConstraintSet>();
        for (int i = 0; i < constraintCount; i++)
        {
            int csPtr = ReadPointer(constraintsOffset + i * 8);
            if (csPtr > 0)
            {
                // Keep a placeholder for anything unparsed so the array index stays aligned with
                // what m_constraintExecution addresses - see HclUnsupportedConstraintSet.
                var cs = DeserializeConstraintSet(csPtr, particleCount)
                         ?? new HclUnsupportedConstraintSet { Name = ReadStringPointer(csPtr + 24) };
                constraints.Add(cs);
            }
        }
        simCloth.ConstraintSets = constraints.ToArray();

        // offset + 168: m_collidableTransformMap:
        //   +0 transformSetIndex, +8 transformIndices, +24 boneFromCollidable offsets.
        // The real runtime computes worldFromBone * boneFromCollidable; in System.Numerics' row-
        // vector convention that is offset * boneWorld. Using hclCollidable.m_transform itself as
        // the offset is wrong: that member is the collider's current/model-space placement.
        simCloth.CollidableTransformSetIndex = ReadInt32(offset + 168);
        simCloth.CollidableTransformIndices = ReadUInt32Array(offset + 176);
        var (colOffsetsOffset, colOffsetsCount) = ReadArrayHeader(offset + 192);
        var colOffsets = new Matrix4x4[colOffsetsCount];
        for (int i = 0; i < colOffsetsCount; i++)
            colOffsets[i] = ReadMatrix4x4(colOffsetsOffset + i * 64);
        simCloth.CollidableTransformOffsets = colOffsets;

        // offset + 208: m_perInstanceCollidables
        var (colsOffset, colCount) = ReadArrayHeader(offset + 208);
        var collidables = new List<HclCollidable>();
        for (int i = 0; i < colCount; i++)
        {
            int cPtr = ReadPointer(colsOffset + i * 8);
            if (cPtr > 0)
            {
                collidables.Add(DeserializeCollidable(cPtr));
            }
        }
        simCloth.Collidables = collidables.ToArray();

        // offset + 224: m_maxParticleRadius; +232: m_staticCollisionMasks. Havok uses bit c of
        // each particle's mask to decide whether per-instance collidable c may touch it. Ignoring
        // this array turns every body capsule into a collider for every hair/cape particle.
        simCloth.StaticCollisionMasks = ReadUInt32Array(offset + 232);

        // offset + 248: m_actions (hkArray<const hclAction*>)
        if ((Environment.GetEnvironmentVariable("WRS_DEBUG_ACTIONS") ?? Environment.GetEnvironmentVariable("MARROW_DEBUG_ACTIONS")) == "1")
        {
            var (actOffset, actCount) = ReadArrayHeader(offset + 248);
            Console.WriteLine($"  [Actions {simCloth.Name}] count={actCount}");
            for (int i = 0; i < actCount; i++)
            {
                int actPtr = ReadPointer(actOffset + i * 8);
                if (actPtr <= 0) continue;
                string actName = ReadStringPointer(actPtr + 24);
                Console.WriteLine($"    action[{i}] name='{actName}'");
            }
        }

        // offset + 264: m_totalMass
        simCloth.TotalMass = ReadFloat(offset + 264);

        if ((Environment.GetEnvironmentVariable("WRS_DEBUG_TRANSFERMOTION") ?? Environment.GetEnvironmentVariable("MARROW_DEBUG_TRANSFERMOTION")) == "1")
        {
            Console.WriteLine($"  [TransferMotion raw {simCloth.Name}]");
            for (int k = 268; k < 352; k += 4)
            {
                float f = ReadFloat(offset + k);
                uint u = ReadUInt32(offset + k);
                Console.WriteLine($"    @{k}: float={f:G6} uint={u}");
            }
        }

        // offset + 352: m_triangleIndices
        var (trisOffset, triCount) = ReadArrayHeader(offset + 352);
        var triangleIndices = new ushort[triCount];
        for (int i = 0; i < triCount; i++)
        {
            triangleIndices[i] = ReadUInt16(trisOffset + i * 2);
        }
        simCloth.TriangleIndices = triangleIndices;

        // Pinch detection follows the two triangle arrays in hclSimClothData. This changes
        // contact selection when multiple expanded colliders overlap, so preserve it rather
        // than inferring the setting from the shared hclCollidable template.
        simCloth.PinchDetectionEnabled = ReadBool(offset + 384);
        var (particlePinchOffset, particlePinchCount) = ReadArrayHeader(offset + 392);
        var particlePinch = new bool[particlePinchCount];
        for (int i = 0; i < particlePinchCount; i++)
            particlePinch[i] = ReadBool(particlePinchOffset + i);
        simCloth.PerParticlePinchDetectionEnabled = particlePinch;

        var (colliderPinchOffset, colliderPinchCount) = ReadArrayHeader(offset + 408);
        var colliderPinch = new HclCollidablePinchingData[colliderPinchCount];
        for (int i = 0; i < colliderPinchCount; i++)
        {
            int entry = colliderPinchOffset + i * 8;
            colliderPinch[i] = new HclCollidablePinchingData(
                ReadBool(entry), unchecked((sbyte)_data[entry + 1]), ReadFloat(entry + 4));
        }
        simCloth.CollidablePinchingData = colliderPinch;

        // hclVirtualCollisionPointsData begins at +432; m_numVCPoints is at +16. A collidable's
        // capability flag is insufficient by itself: Havok enables the VCP pass only when this is
        // nonzero. Zelda's cloth pieces all author zero.
        simCloth.NumVirtualCollisionPoints = ReadUInt16(offset + 448);

        return simCloth;
    }

    /// <summary>
    /// Some cloth pieces (first found on Zelda's SimulationMesh_ApronAcs/ChestAcs, confirmed again
    /// on Ganondorf's SimulationMesh_BackSide) author their HclStandardLinkConstraintSet with a
    /// real per-link record that is 16 bytes, not the 12-byte
    /// <c>(particleA:u16, particleB:u16, restLength:f32, stiffness:f32)</c> layout every other
    /// tested StandardLinkConstraintSet uses - reading it at 12-byte stride drifts out of alignment
    /// with the true 16-byte records (their least common multiple is 48 bytes = 4 assumed-stride
    /// reads = 3 real records), which is why one entry in every four still came out looking
    /// structurally sane (particle indices in range) while the other three came out reading garbage
    /// tail bytes as if they were fresh (particleA, particleB) pairs - tens of thousands as a
    /// particle index. Confirmed directly against the raw bytes on BackSide
    /// (<c>DumpGanondorfBackSideRawBytes</c> during this investigation): the true record is
    /// <c>(particleA:u16, particleB:u16, restLength:f32, restLength_again:f32, stiffness:f32)</c> -
    /// the SAME restLength float genuinely repeated once (padding/alignment, not corruption) before
    /// the real stiffness float, which is why the naively-12-byte-read "every 4th" entry always
    /// showed restLength==stiffness exactly (it was reading the restLength duplicate as stiffness).
    /// The real stiffness values recovered this way (0.0007-0.0064) land in the same tiny range as
    /// every other hair piece in this same file (e.g. Hair_A: 0.0001-0.0005) confirming the fix
    /// against independent, uncorrupted data in the very same file - the previous 12-byte reading
    /// had it at 0.13-0.26, a value 100-1000x too large that exploded the solver in a single step.
    /// Detected here (rather than assumed) via a >=10000 particle index, which is never legitimate
    /// for any real cloth piece in this project (all have well under a thousand particles) - and,
    /// when found, this re-reads the SAME byte range at the real 16-byte stride instead of
    /// discarding 3 of every 4 real links (an earlier attempt at this fix kept only 1-in-4, which
    /// happened to still be "sane" by the particle-index check but was reading the SAME wrong
    /// stiffness duplicate, not a genuine recovery).
    /// </summary>
    private StandardLink[] ReadStandardLinks(int linksOffset, int linkCount, int particleCountHint)
    {
        var links = new StandardLink[linkCount];
        for (int i = 0; i < linkCount; i++)
        {
            int lOff = linksOffset + i * 12;
            links[i] = new StandardLink(ReadUInt16(lOff), ReadUInt16(lOff + 2), ReadFloat(lOff + 4), ReadFloat(lOff + 8));
        }

        int garbageThreshold = particleCountHint > 0 ? Math.Max(particleCountHint * 4, 64) : 10000;
        int garbageCount = links.Count(l => l.ParticleA >= garbageThreshold || l.ParticleB >= garbageThreshold);
        if (garbageCount < linkCount / 4) return links; // plain 12-byte layout, nothing to fix

        // Re-derive the count for the REAL 16-byte stride from the same byte range the (wrong)
        // 12-byte header count already committed to: linkCount * 12 bytes total / 16 bytes each.
        int totalBytes = linkCount * 12;
        int realCount = totalBytes / 16;
        if (realCount <= 0 || realCount * 16 != totalBytes) return links; // doesn't fit the known real layout; leave as-is rather than guess further

        var real = new StandardLink[realCount];
        for (int i = 0; i < realCount; i++)
        {
            int lOff = linksOffset + i * 16;
            real[i] = new StandardLink(ReadUInt16(lOff), ReadUInt16(lOff + 2), ReadFloat(lOff + 4), ReadFloat(lOff + 12));
        }
        return real;
    }

    private HclConstraintSet? DeserializeConstraintSet(int offset, int particleCountHint = 0)
    {
        string name = ReadStringPointer(offset + 24);
        uint constraintId = ReadUInt32(offset + 32);
        uint type = ReadUInt32(offset + 36);

        var (linksOffset, linkCount) = ReadArrayHeader(offset + 40);

        if (name.Contains("Standard Links") || name.Contains("StandardLink"))
        {
            return new HclStandardLinkConstraintSet
            {
                Name = name,
                ConstraintId = constraintId,
                Type = type,
                Links = ReadStandardLinks(linksOffset, linkCount, particleCountHint)
            };
        }

        if (name.Contains("Stretch Link") || name.Contains("StretchLink"))
        {
            var links = new StandardLink[linkCount];
            for (int i = 0; i < linkCount; i++)
            {
                int lOff = linksOffset + i * 12;
                ushort a = ReadUInt16(lOff);
                ushort b = ReadUInt16(lOff + 2);
                float restLength = ReadFloat(lOff + 4);
                float stiffness = ReadFloat(lOff + 8);
                links[i] = new StandardLink(a, b, restLength, stiffness);
            }
            return new HclStretchLinkConstraintSet
            {
                Name = name,
                ConstraintId = constraintId,
                Type = type,
                Links = links
            };
        }

        if (name.Contains("Bend Links") || name.Contains("BendLink"))
        {
            // Field order matches hclBendLinkConstraintSet::Link exactly (see BendLink's own
            // remarks): bendMinLength, stretchMaxLength, bendStiffness, stretchStiffness - NOT
            // "min/max distance and a single stiffness" as an earlier version of this read them.
            var links = new BendLink[linkCount];
            for (int i = 0; i < linkCount; i++)
            {
                int lOff = linksOffset + i * 20;
                ushort a = ReadUInt16(lOff);
                ushort b = ReadUInt16(lOff + 2);
                float bendMinLength = ReadFloat(lOff + 4);
                float stretchMaxLength = ReadFloat(lOff + 8);
                float bendStiffness = ReadFloat(lOff + 12);
                float stretchStiffness = ReadFloat(lOff + 16);
                links[i] = new BendLink(a, b, bendMinLength, stretchMaxLength, bendStiffness, stretchStiffness);
            }
            return new HclBendLinkConstraintSet
            {
                Name = name,
                ConstraintId = constraintId,
                Type = type,
                Links = links
            };
        }

        if (name.Contains("Bend Stiffness") || name.Contains("BendStiffness"))
        {
            var links = new BendStiffnessLink[linkCount];
            for (int i = 0; i < linkCount; i++)
            {
                int lOff = linksOffset + i * 32;
                float wA = ReadFloat(lOff);
                float wB = ReadFloat(lOff + 4);
                float wC = ReadFloat(lOff + 8);
                float wD = ReadFloat(lOff + 12);
                float bendStiff = ReadFloat(lOff + 16);
                float restCurv = ReadFloat(lOff + 20);
                ushort a = ReadUInt16(lOff + 24);
                ushort b = ReadUInt16(lOff + 26);
                ushort c = ReadUInt16(lOff + 28);
                ushort d = ReadUInt16(lOff + 30);
                links[i] = new BendStiffnessLink(wA, wB, wC, wD, bendStiff, restCurv, a, b, c, d);
            }
            return new HclBendStiffnessConstraintSet
            {
                Name = name,
                ConstraintId = constraintId,
                Type = type,
                Links = links,
                MaxRestPoseHeightSq = ReadFloat(offset + 56),
                ClampBendStiffness = ReadBool(offset + 60),
                UseRestPoseConfig = ReadBool(offset + 61)
            };
        }

        if (name.Contains("Local Range") || name.Contains("LocalRange"))
        {
            // Real layout (hclLocalRangeConstraintSet.h), all four arrays present, exactly one used:
            //   @40  m_localConstraints                 (LocalConstraint,                16B stride)
            //   @56  m_localStiffnessConstraints        (LocalStiffnessConstraint,       20B stride)
            //   @72  m_localCapsuleConstraints          (LocalCapsuleConstraint,         20B stride)
            //   @88  m_localCapsuleStiffnessConstraints (LocalCapsuleStiffnessConstraint,24B stride)
            //   @104 m_referenceMeshBufferIdx   @108 m_stiffness   @112 m_shapeType
            // The CPU solver's own dispatch is "m_localConstraints if non-empty, else
            // m_localStiffnessConstraints" - reproduced here. Only reading the first array is what
            // silently dropped every local-range tether on Zelda's back hair.
            var (plainOffset, plainCount) = ReadArrayHeader(offset + 40);
            var (stiffOffset, stiffCount) = ReadArrayHeader(offset + 56);

            LocalRangeConstraint[] locals;
            if (plainCount > 0)
            {
                locals = new LocalRangeConstraint[plainCount];
                for (int i = 0; i < plainCount; i++)
                {
                    int lOff = plainOffset + i * 16;
                    locals[i] = new LocalRangeConstraint(
                        ReadUInt16(lOff),
                        ReadUInt16(lOff + 2),
                        ReadFloat(lOff + 4),
                        ReadFloat(lOff + 8),
                        ReadFloat(lOff + 12),
                        1.0f); // real LocalConstraint::getStiffness() is hardcoded to 1.0f
                }
            }
            else
            {
                locals = new LocalRangeConstraint[stiffCount];
                for (int i = 0; i < stiffCount; i++)
                {
                    int lOff = stiffOffset + i * 20;
                    locals[i] = new LocalRangeConstraint(
                        ReadUInt16(lOff),
                        ReadUInt16(lOff + 2),
                        ReadFloat(lOff + 4),
                        ReadFloat(lOff + 8),
                        ReadFloat(lOff + 12),
                        ReadFloat(lOff + 16));
                }
            }

            return new HclLocalRangeConstraintSet
            {
                Name = name,
                ConstraintId = constraintId,
                Type = type,
                LocalConstraints = locals,
                ReferenceMeshBufferIdx = ReadUInt32(offset + 104),
                Stiffness = ReadFloat(offset + 108),
                ShapeType = ReadUInt32(offset + 112)
            };
        }

        if (name.Contains("Compressible"))
        {
            var links = new CompressibleLink[linkCount];
            for (int i = 0; i < linkCount; i++)
            {
                int lOff = linksOffset + i * 16;
                ushort a = ReadUInt16(lOff);
                ushort b = ReadUInt16(lOff + 2);
                float restL = ReadFloat(lOff + 4);
                float compL = ReadFloat(lOff + 8);
                float stiffness = ReadFloat(lOff + 12);
                links[i] = new CompressibleLink(a, b, restL, compL, stiffness);
            }
            return new HclCompressibleLinkConstraintSet
            {
                Name = name,
                ConstraintId = constraintId,
                Type = type,
                Links = links
            };
        }

        return null;
    }

    private HclOperator? DeserializeOperator(int offset)
    {
        string name = ReadStringPointer(offset + 24);
        uint opId = ReadUInt32(offset + 32);
        uint type = ReadUInt32(offset + 36);

        if (name.Contains("Simulate"))
        {
            uint simIdx = ReadUInt32(offset + 72);
            var (cfgOffset, cfgCount) = ReadArrayHeader(offset + 80);
            byte subSteps = 1;
            byte iters = 1;
            bool useAllCol = true;
            bool adaptStiff = false;
            bool[] instanceCollidablesUsed = Array.Empty<bool>();

            int[] constraintExecution = Array.Empty<int>();
            if (cfgCount > 0)
            {
                // Config layout: m_name @0, m_constraintExecution @8, m_instanceCollidablesUsed @24,
                // m_subSteps @40, m_numberOfSolveIterations @41, m_useAllInstanceCollidables @42,
                // m_adaptConstraintStiffness @43.
                var (ceOff, ceCount) = ReadArrayHeader(cfgOffset + 8);
                constraintExecution = new int[ceCount];
                for (int k = 0; k < ceCount; k++) constraintExecution[k] = ReadInt32(ceOff + k * 4);

                var (usedOff, usedCount) = ReadArrayHeader(cfgOffset + 24);
                instanceCollidablesUsed = new bool[usedCount];
                for (int k = 0; k < usedCount; k++) instanceCollidablesUsed[k] = ReadBool(usedOff + k);
            }

            if (cfgCount > 0)
            {
                subSteps = _data[cfgOffset + 40];
                iters = _data[cfgOffset + 41];
                useAllCol = _data[cfgOffset + 42] != 0;
                adaptStiff = _data[cfgOffset + 43] != 0;
            }

            return new HclSimulateOperator
            {
                Name = name,
                OperatorId = opId,
                Type = type,
                SimClothIndex = simIdx,
                SubSteps = subSteps,
                NumberOfSolveIterations = iters,
                ConstraintExecution = constraintExecution,
                UseAllInstanceCollidables = useAllCol,
                InstanceCollidablesUsed = instanceCollidablesUsed,
                AdaptConstraintStiffness = adaptStiff
            };
        }

        if (name.Contains("MoveFixedParticles") || name.Contains("MoveParticles"))
        {
            var (vpOffset, vpCount) = ReadArrayHeader(offset + 72);
            uint simIdx = ReadUInt32(offset + 88);
            uint refBuf = ReadUInt32(offset + 92);

            var pairs = new VertexParticlePair[vpCount];
            for (int i = 0; i < vpCount; i++)
            {
                ushort v = ReadUInt16(vpOffset + i * 4);
                ushort p = ReadUInt16(vpOffset + i * 4 + 2);
                pairs[i] = new VertexParticlePair(v, p);
            }

            return new HclMoveParticlesOperator
            {
                Name = name,
                OperatorId = opId,
                Type = type,
                SimClothIndex = simIdx,
                RefBufferIndex = refBuf,
                VertexParticlePairs = pairs
            };
        }

        if (name.Contains("VertexGather") || name.Contains("GatherAllVertices"))
        {
            // hclGatherAllVerticesOperator: m_vertexInputFromVertexOutput @72 (hkArray<hkInt16>),
            // m_inputBufferIdx @88, m_outputBufferIdx @92, m_gatherNormals @96, m_partialGather @97.
            var (mapOffset, mapCount) = ReadArrayHeader(offset + 72);
            var mapping = new short[mapCount];
            for (int i = 0; i < mapCount; i++)
            {
                mapping[i] = ReadInt16(mapOffset + i * 2);
            }

            return new HclGatherAllVerticesOperator
            {
                Name = name,
                OperatorId = opId,
                Type = type,
                VertexInputFromVertexOutput = mapping,
                InputBufferIndex = ReadUInt32(offset + 88),
                OutputBufferIndex = ReadUInt32(offset + 92),
                GatherNormals = _data[offset + 96] != 0,
                PartialGather = _data[offset + 97] != 0
            };
        }

        if (name.Contains("MeshBone"))
        {
            uint inBuf = ReadUInt32(offset + 72);
            uint outTs = ReadUInt32(offset + 76);
            var (tbOffset, tbCount) = ReadArrayHeader(offset + 80);
            var (ltOffset, ltCount) = ReadArrayHeader(offset + 96);
            uint boneAxis = ReadUInt32(offset + 112);

            var pairs = new TriangleBonePair[tbCount];
            for (int i = 0; i < tbCount; i++)
            {
                ushort bone = ReadUInt16(tbOffset + i * 4);
                ushort tri = ReadUInt16(tbOffset + i * 4 + 2);
                pairs[i] = new TriangleBonePair(bone, tri);
            }

            var localTransforms = new Matrix4x4[ltCount];
            for (int i = 0; i < ltCount; i++)
            {
                localTransforms[i] = ReadMatrix4x4(ltOffset + i * 64);
            }

            return new HclSimpleMeshBoneDeformOperator
            {
                Name = name,
                OperatorId = opId,
                Type = type,
                InputBufferIndex = inBuf,
                OutputTransformSetIndex = outTs,
                TriangleBonePairs = pairs,
                LocalBoneTransforms = localTransforms,
                BoneAxis = boneAxis
            };
        }

        if (name.Contains("Skin"))
        {
            var (bfsmOffset, bfsmCount) = ReadArrayHeader(offset + 72);
            var (tsOffset, tsCount) = ReadArrayHeader(offset + 88);
            uint outBuf = ReadUInt32(offset + 104);
            uint tsIdx = ReadUInt32(offset + 108);

            var transforms = new Matrix4x4[bfsmCount];
            for (int i = 0; i < bfsmCount; i++)
            {
                transforms[i] = ReadMatrix4x4(bfsmOffset + i * 64);
            }

            var subset = new ushort[tsCount];
            for (int i = 0; i < tsCount; i++)
            {
                subset[i] = ReadUInt16(tsOffset + i * 2);
            }

            return new HclObjectSpaceSkinOperator
            {
                Name = name,
                OperatorId = opId,
                Type = type,
                BoneFromSkinMeshTransforms = transforms,
                TransformSubset = subset,
                OutputBufferIndex = outBuf,
                TransformSetIndex = tsIdx,
                Deformer = DeserializeObjectSpaceDeformer(offset, type)
            };
        }

        return null;
    }

    private HclObjectSpaceDeformer DeserializeObjectSpaceDeformer(int operatorOffset, uint operatorType)
    {
        // hclObjectSpaceSkinOperator embeds hclObjectSpaceDeformer at +112. Its arrays are ordered
        // 8..1 blends. Derived P/PN/PNT/PNTB operators append packed and unpacked local blocks at
        // +264/+280. All local block variants begin with the same 16 positions; only their stride
        // changes as normals/tangents are appended.
        int deformerOffset = operatorOffset + 112;
        var blocksByInfluence = new HclObjectSpaceBlendBlock[8][];
        int[] blockStrides = [64, 128, 176, 224, 352, 416, 480, 544];

        for (int influences = 8; influences >= 1; influences--)
        {
            int headerOffset = deformerOffset + (8 - influences) * 16;
            var (dataOffset, count) = ReadArrayHeader(headerOffset);
            int stride = blockStrides[influences - 1];
            if (!IsArrayRangeValid(dataOffset, count, stride))
            {
                blocksByInfluence[influences - 1] = Array.Empty<HclObjectSpaceBlendBlock>();
                continue;
            }

            var blocks = new HclObjectSpaceBlendBlock[count];
            for (int b = 0; b < count; b++)
            {
                int p = dataOffset + b * stride;
                var vertices = new ushort[16];
                var boneIndices = new ushort[16 * influences];
                var weights = new float[16 * influences];
                for (int i = 0; i < 16; i++) vertices[i] = ReadUInt16(p + i * 2);
                p += 32;
                for (int i = 0; i < boneIndices.Length; i++) boneIndices[i] = ReadUInt16(p + i * 2);
                p += boneIndices.Length * 2;
                if (influences >= 5)
                {
                    for (int i = 0; i < weights.Length; i++) weights[i] = ReadUInt16(p + i * 2) / 65535f;
                }
                else if (influences > 1)
                {
                    for (int i = 0; i < weights.Length; i++) weights[i] = _data[p + i] / 255f;
                }
                else
                {
                    Array.Fill(weights, 1f);
                }
                blocks[b] = new HclObjectSpaceBlendBlock
                {
                    InfluenceCount = influences,
                    VertexIndices = vertices,
                    BoneIndices = boneIndices,
                    BoneWeights = weights
                };
            }
            blocksByInfluence[influences - 1] = blocks;
        }

        var (controlOffset, controlCount) = ReadArrayHeader(deformerOffset + 128);
        byte[] controls = IsArrayRangeValid(controlOffset, controlCount, 1)
            ? _data.AsSpan(controlOffset, controlCount).ToArray()
            : Array.Empty<byte>();

        // Enum values 22..25 are OBJECTSPACE_SKIN_P/PN/PNT/PNTB.
        int componentCount = operatorType is >= 22 and <= 25 ? (int)operatorType - 21 : 1;
        var packed = ReadLocalPositionBlocks(operatorOffset + 264, componentCount * 128, packed: true);
        var unpacked = ReadLocalPositionBlocks(operatorOffset + 280, componentCount * 256, packed: false);

        return new HclObjectSpaceDeformer
        {
            BlendBlocks = blocksByInfluence,
            ControlBytes = controls,
            PackedLocalPositions = packed,
            UnpackedLocalPositions = unpacked,
            StartVertexIndex = ReadUInt16(deformerOffset + 144),
            EndVertexIndex = ReadUInt16(deformerOffset + 146),
            PartialWrite = ReadBool(deformerOffset + 148)
        };
    }

    private Vector3[][] ReadLocalPositionBlocks(int headerOffset, int stride, bool packed)
    {
        var (dataOffset, count) = ReadArrayHeader(headerOffset);
        if (!IsArrayRangeValid(dataOffset, count, stride)) return Array.Empty<Vector3[]>();
        var result = new Vector3[count][];
        for (int b = 0; b < count; b++)
        {
            var positions = new Vector3[16];
            int p = dataOffset + b * stride;
            for (int i = 0; i < 16; i++)
            {
                if (packed)
                {
                    short x = ReadInt16(p + i * 8);
                    short y = ReadInt16(p + i * 8 + 2);
                    short z = ReadInt16(p + i * 8 + 4);
                    ushort exponentHighBits = ReadUInt16(p + i * 8 + 6);
                    // hkPackedVector3::unpack uses setCombineHead16To32: each signed component is
                    // placed in the HIGH half of an int32 before float conversion, and the fourth
                    // ushort is likewise placed in the high half to reconstruct the IEEE scale.
                    // Merely sign-extending the shorts (the tempting implementation) is off by a
                    // factor encoded into that exponent and visibly displaces every anchor.
                    float scale = BitConverter.Int32BitsToSingle(exponentHighBits << 16);
                    const float componentScale = 65536f;
                    positions[i] = new Vector3(x * componentScale * scale, y * componentScale * scale, z * componentScale * scale);
                }
                else
                {
                    positions[i] = ReadVector3(p + i * 16);
                }
            }
            result[b] = positions;
        }
        return result;
    }

    private bool IsArrayRangeValid(int offset, int count, int stride) =>
        offset > 0 && count >= 0 && stride > 0 && (long)offset + (long)count * stride <= _data.Length;

    private HclBufferDefinition DeserializeBufferDefinition(int offset)
    {
        return new HclBufferDefinition
        {
            MeshName = ReadStringPointer(offset + 24),
            BufferName = ReadStringPointer(offset + 32),
            Type = ReadUInt32(offset + 40),
            SubType = ReadUInt32(offset + 44),
            NumVertices = ReadUInt32(offset + 48),
            NumTriangles = ReadUInt32(offset + 52)
        };
    }

    private HclTransformSetDefinition DeserializeTransformSetDefinition(int offset)
    {
        return new HclTransformSetDefinition
        {
            Name = ReadStringPointer(offset + 24),
            Type = ReadInt32(offset + 32),
            NumTransforms = ReadUInt32(offset + 36)
        };
    }

    private HkaAnimationContainer DeserializeAnimationContainer(int offset)
    {
        var anim = new HkaAnimationContainer();

        // offset + 24: m_skeletons
        var (skelsOffset, skelCount) = ReadArrayHeader(offset + 24);
        for (int i = 0; i < skelCount; i++)
        {
            int skelPtr = ReadPointer(skelsOffset + i * 8);
            if (skelPtr > 0)
            {
                string skelName = ReadStringPointer(skelPtr + 24);
                var skel = new HkaSkeleton { Name = skelName };

                // m_parentIndices array at skelPtr + 32
                var (parentsOffset, parentCount) = ReadArrayHeader(skelPtr + 32);
                // m_bones array at skelPtr + 48
                var (bonesOffset, boneCount) = ReadArrayHeader(skelPtr + 48);
                // m_referencePose array at skelPtr + 64
                var (refPoseOffset, refPoseCount) = ReadArrayHeader(skelPtr + 64);

                for (int b = 0; b < boneCount; b++)
                {
                    int boneOffset = bonesOffset + b * 16;
                    string bName = ReadStringPointer(boneOffset);
                    short parentIdx = b < parentCount ? ReadInt16(parentsOffset + b * 2) : (short)-1;
                    skel.Bones.Add(new HkaBone { Name = bName, ParentIndex = parentIdx });
                }

                for (int r = 0; r < refPoseCount; r++)
                {
                    int poseOffset = refPoseOffset + r * 48; // hkQsTransformf is 48 bytes
                    Vector3 trans = ReadVector3(poseOffset);
                    Vector4 rotVec = ReadVector4(poseOffset + 16);
                    Quaternion rot = new Quaternion(rotVec.X, rotVec.Y, rotVec.Z, rotVec.W);
                    Vector3 scale = ReadVector3(poseOffset + 32);
                    skel.ReferencePose.Add(Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(trans));
                }

                anim.Skeletons.Add(skel);
            }
        }

        return anim;
    }

    #region Helper Binary Readers

    private int ReadPointer(int offset)
    {
        if (offset + 8 > _data.Length) return 0;
        return (int)BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(offset, 8));
    }

    private string ReadStringPointer(int offset)
    {
        int strOffset = ReadPointer(offset);
        if (strOffset <= 0 || strOffset >= _data.Length)
            return string.Empty;

        int end = Array.IndexOf(_data, (byte)0, strOffset);
        if (end < 0) end = _data.Length;

        return Encoding.UTF8.GetString(_data, strOffset, end - strOffset);
    }

    private (int DataOffset, int Count) ReadArrayHeader(int offset)
    {
        if (offset + 12 > _data.Length) return (0, 0);
        int dataOffset = ReadPointer(offset);
        int count = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset + 8, 4));
        return (dataOffset, Math.Max(0, count));
    }

    private uint[] ReadUInt32Array(int offset)
    {
        var (dataOffset, count) = ReadArrayHeader(offset);
        if (count <= 0 || dataOffset <= 0 || dataOffset + count * 4 > _data.Length)
            return Array.Empty<uint>();

        var result = new uint[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = ReadUInt32(dataOffset + i * 4);
        }
        return result;
    }


    private ushort ReadUInt16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset, 2));
    private short ReadInt16(int offset) => BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(offset, 2));
    private uint ReadUInt32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset, 4));
    private int ReadInt32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset, 4));
    private float ReadFloat(int offset) => BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(offset, 4));
    private bool ReadBool(int offset) => _data[offset] != 0;

    private Vector3 ReadVector3(int offset)
    {
        float x = ReadFloat(offset);
        float y = ReadFloat(offset + 4);
        float z = ReadFloat(offset + 8);
        return new Vector3(x, y, z);
    }

    private Vector4 ReadVector4(int offset)
    {
        float x = ReadFloat(offset);
        float y = ReadFloat(offset + 4);
        float z = ReadFloat(offset + 8);
        float w = ReadFloat(offset + 12);
        return new Vector4(x, y, z, w);
    }

    private Matrix4x4 ReadMatrix4x4(int offset)
    {
        return new Matrix4x4(
            ReadFloat(offset), ReadFloat(offset + 4), ReadFloat(offset + 8), ReadFloat(offset + 12),
            ReadFloat(offset + 16), ReadFloat(offset + 20), ReadFloat(offset + 24), ReadFloat(offset + 28),
            ReadFloat(offset + 32), ReadFloat(offset + 36), ReadFloat(offset + 40), ReadFloat(offset + 44),
            ReadFloat(offset + 48), ReadFloat(offset + 52), ReadFloat(offset + 56), ReadFloat(offset + 60)
        );
    }

    private Matrix4x4 ReadTransform(int offset)
    {
        Vector4 col0 = ReadVector4(offset);
        Vector4 col1 = ReadVector4(offset + 16);
        Vector4 col2 = ReadVector4(offset + 32);
        Vector4 col3 = ReadVector4(offset + 48);

        return new Matrix4x4(
            col0.X, col0.Y, col0.Z, 0f,
            col1.X, col1.Y, col1.Z, 0f,
            col2.X, col2.Y, col2.Z, 0f,
            col3.X, col3.Y, col3.Z, 1f
        );
    }

    #endregion
}

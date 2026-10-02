# blender -b --python pose.py -- <rigged.blend> <render_dir>
# Batting-stance deformation check. Bone axes: local X = hinge (+X flexes arms forward), Y = twist.
import sys, os, math, bpy
src, rdir = sys.argv[sys.argv.index("--") + 1:][:2]
os.makedirs(rdir, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene; cam = sc.camera
arm = bpy.data.objects["BatterRig"]; mesh = bpy.data.objects["Batter"]
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "TEXTURE"; sc.display.shading.show_xray = False
h = 1.902
pb = arm.pose.bones
def pose(rot):
    for b in pb: b.rotation_mode = "XYZ"; b.rotation_euler = (0, 0, 0)
    for n, e in rot.items(): pb[n].rotation_euler = [math.radians(a) for a in e]
    bpy.context.view_layer.update()
def shot(name, yaw_deg, scale, z, x=0.0):
    a = math.radians(yaw_deg); cam.data.ortho_scale = scale
    cam.location = (math.sin(a) * 5 + math.cos(a) * x, math.cos(a) * -5 + math.sin(a) * x, z)
    cam.rotation_euler = (math.radians(90), 0, a)
    sc.render.filepath = os.path.join(rdir, name + ".png"); bpy.ops.render.render(write_still=True)

stance = {
    "Hips": (0, -15, 0), "Spine": (5, -10, 0), "Chest": (5, -15, 0), "Head": (0, 30, 0),
    "UpperArm.R": (60, 0, 25), "LowerArm.R": (95, 0, 0), "Hand.R": (0, 0, 0),
    "UpperArm.L": (70, 0, -35), "LowerArm.L": (80, 0, 0),
    "UpperLeg.L": (20, 0, 5), "LowerLeg.L": (-35, 0, 0), "Foot.L": (15, 0, 0),
    "UpperLeg.R": (20, 0, -5), "LowerLeg.R": (-35, 0, 0), "Foot.R": (15, 0, 0),
    "Tail1": (0, 0, -15), "Tail2": (10, 0, 0), "Ear.L": (-10, 0, 0),
}
pose(stance)
shot("stance_front", 0, h * 1.1, h / 2)
shot("stance_34", -40, h * 1.1, h / 2)
shot("stance_side", 90, h * 1.1, h / 2)
shot("stance_shoulders", -40, h * 0.45, h * 0.5)
pose({"UpperLeg.L": (70, 0, 0), "LowerLeg.L": (-100, 0, 0), "UpperArm.L": (0, 0, 60), "LowerArm.L": (120, 0, 0)})
shot("extreme_front", 0, h * 1.1, h / 2)
shot("extreme_side", 90, h * 1.1, h / 2)
print("POSE DONE")

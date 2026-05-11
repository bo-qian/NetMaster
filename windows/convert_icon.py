from PIL import Image

def expand2square(pil_img, background_color=(0, 0, 0, 0)):
    """
    将图片填充为正方形，背景透明，图片居中。
    解决长方形图片强制 resize 导致的变形和模糊问题。
    """
    width, height = pil_img.size
    if width == height:
        return pil_img
    elif width > height:
        result = Image.new(pil_img.mode, (width, width), background_color)
        result.paste(pil_img, (0, (width - height) // 2))
        return result
    else:
        result = Image.new(pil_img.mode, (height, height), background_color)
        result.paste(pil_img, ((height - width) // 2, 0))
        return result

# 1. 打开源图片
img_source = Image.open("Debian.png").convert("RGBA")

# 【关键步骤】确保源图是正方形画布，防止变形
img_square = expand2square(img_source)

# 检查源图是否足够大
if img_square.size[0] < 256:
    print("警告：源图片分辨率小于 256x256，大尺寸图标可能会模糊！")

# 2. 定义需要的尺寸
# 将小尺寸和大尺寸分开处理也可以，但通常先尝试统一用高质量滤镜
sizes_to_generate = [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
ico_images = []

for size in sizes_to_generate:
    # 对于所有尺寸，在正方形化之后，LANCZOS 通常是下采样的最佳选择。
    # 它比 BICUBIC 提供更好的抗锯齿效果，这对于 16x16 至关重要。
    # 如果源图足够大，它也不会导致大尺寸模糊。
    resized_img = img_square.resize(size, Image.Resampling.LANCZOS)
    ico_images.append(resized_img)

# 3. 保存为 ICO
# 使用 append_images 将所有帧合并
ico_images[0].save(
    "Debian_Ultimate.ico",
    format='ICO',
    sizes=[img.size for img in ico_images],
    append_images=ico_images[1:]
)

print("终极版 Debian_Ultimate.ico 已生成！请检查效果。")
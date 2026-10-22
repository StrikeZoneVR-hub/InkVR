import re, sys, os
root = sys.argv[1]
src = open(os.path.join(root, 'vendor/bcdec.h'), encoding='utf-8').read()
start = src.index('static unsigned char partition_sets[2][64][4][4] = {')
end = src.index('static int aWeight2[]', start)
block = src[start:end].split('=', 1)[1]
block = re.sub(r'/\*.*?\*/', '', block, flags=re.S)
nums = [int(x) for x in re.findall(r'\b\d+\b', block)]
assert len(nums) == 2 * 64 * 16, len(nums)
rows = ['            ' + ', '.join(str(n) for n in nums[i:i + 16]) + ',' for i in range(0, len(nums), 16)]
template = open(os.path.join(root, 'tools/TextureDecoder.cs.template'), encoding='utf-8').read()
out = template.replace('/*PARTITIONS*/', '\n'.join(rows))
open(os.path.join(root, 'src/InkVR.Launcher/Game/TextureDecoder.cs'), 'w', encoding='utf-8', newline='\n').write(out)
print('ok', len(nums))

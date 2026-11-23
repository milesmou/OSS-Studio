import type { BucketProfile, ObjectEntry } from "./types";

export function uploadPercentage(current: number, total: number): number {
  return total > 0 ? Math.min(99, Math.floor((current / total) * 100)) : 0;
}

export interface RegionOption {
  code: string;
  name: string;
  endpoint: string;
}

export const REGIONS: RegionOption[] = [
  { code: "cn-hangzhou", name: "华东 1（杭州）", endpoint: "oss-cn-hangzhou.aliyuncs.com" },
  { code: "cn-shanghai", name: "华东 2（上海）", endpoint: "oss-cn-shanghai.aliyuncs.com" },
  { code: "cn-nanjing", name: "华东 5（南京-本地地域，停止服务中）", endpoint: "oss-cn-nanjing.aliyuncs.com" },
  { code: "cn-fuzhou", name: "华东 6（福州-本地地域，退役中）", endpoint: "oss-cn-fuzhou.aliyuncs.com" },
  { code: "cn-wuhan-lr", name: "华中 1（武汉-本地地域）", endpoint: "oss-cn-wuhan-lr.aliyuncs.com" },
  { code: "cn-qingdao", name: "华北 1（青岛）", endpoint: "oss-cn-qingdao.aliyuncs.com" },
  { code: "cn-beijing", name: "华北 2（北京）", endpoint: "oss-cn-beijing.aliyuncs.com" },
  { code: "cn-zhangjiakou", name: "华北 3（张家口）", endpoint: "oss-cn-zhangjiakou.aliyuncs.com" },
  { code: "cn-huhehaote", name: "华北 5（呼和浩特）", endpoint: "oss-cn-huhehaote.aliyuncs.com" },
  { code: "cn-wulanchabu", name: "华北 6（乌兰察布）", endpoint: "oss-cn-wulanchabu.aliyuncs.com" },
  { code: "cn-shenzhen", name: "华南 1（深圳）", endpoint: "oss-cn-shenzhen.aliyuncs.com" },
  { code: "cn-heyuan", name: "华南 2（河源）", endpoint: "oss-cn-heyuan.aliyuncs.com" },
  { code: "cn-guangzhou", name: "华南 3（广州）", endpoint: "oss-cn-guangzhou.aliyuncs.com" },
  { code: "cn-chengdu", name: "西南 1（成都）", endpoint: "oss-cn-chengdu.aliyuncs.com" },
  { code: "cn-zhongwei", name: "西北 1（中卫）", endpoint: "oss-cn-zhongwei.aliyuncs.com" },
  { code: "cn-hongkong", name: "中国香港", endpoint: "oss-cn-hongkong.aliyuncs.com" },
  { code: "rg-china-mainland", name: "无地域属性（中国内地）", endpoint: "oss-rg-china-mainland.aliyuncs.com" },
  { code: "ap-northeast-1", name: "日本（东京）", endpoint: "oss-ap-northeast-1.aliyuncs.com" },
  { code: "ap-northeast-2", name: "韩国（首尔）", endpoint: "oss-ap-northeast-2.aliyuncs.com" },
  { code: "ap-southeast-1", name: "新加坡", endpoint: "oss-ap-southeast-1.aliyuncs.com" },
  { code: "ap-southeast-3", name: "马来西亚（吉隆坡）", endpoint: "oss-ap-southeast-3.aliyuncs.com" },
  { code: "ap-southeast-5", name: "印度尼西亚（雅加达）", endpoint: "oss-ap-southeast-5.aliyuncs.com" },
  { code: "ap-southeast-6", name: "菲律宾（马尼拉）", endpoint: "oss-ap-southeast-6.aliyuncs.com" },
  { code: "ap-southeast-7", name: "泰国（曼谷）", endpoint: "oss-ap-southeast-7.aliyuncs.com" },
  { code: "ap-southeast-8", name: "马来西亚（柔佛）", endpoint: "oss-ap-southeast-8.aliyuncs.com" },
  { code: "eu-central-1", name: "德国（法兰克福）", endpoint: "oss-eu-central-1.aliyuncs.com" },
  { code: "eu-west-1", name: "英国（伦敦）", endpoint: "oss-eu-west-1.aliyuncs.com" },
  { code: "us-west-1", name: "美国（硅谷）", endpoint: "oss-us-west-1.aliyuncs.com" },
  { code: "us-east-1", name: "美国（弗吉尼亚）", endpoint: "oss-us-east-1.aliyuncs.com" },
  { code: "na-south-1", name: "墨西哥", endpoint: "oss-na-south-1.aliyuncs.com" },
  { code: "eu-west-2", name: "法国（巴黎）", endpoint: "oss-eu-west-2.aliyuncs.com" },
  { code: "me-east-1", name: "阿联酋（迪拜）", endpoint: "oss-me-east-1.aliyuncs.com" },
  { code: "me-central-1", name: "沙特（利雅得-合作伙伴运营）", endpoint: "oss-me-central-1.aliyuncs.com" },
];

export function regionEndpoint(code: string): string {
  return REGIONS.find((region) => region.code === code)?.endpoint ?? `oss-${code}.aliyuncs.com`;
}

export function regionName(code: string): string {
  return REGIONS.find((region) => region.code === code)?.name ?? code;
}

export function bucketLabel(bucket: BucketProfile): string {
  return bucket.DisplayName.trim() || bucket.Name;
}

export function normalizePrefix(value: string): string {
  const trimmed = value.trim().replace(/^\/+|\/+$/g, "");
  return trimmed ? `${trimmed}/` : "";
}

export function parentPrefix(prefix: string, rootPrefix: string): string {
  const normalized = normalizePrefix(prefix);
  if (normalized === rootPrefix || normalized.length <= rootPrefix.length) {
    return rootPrefix;
  }
  const withoutTrailing = normalized.slice(0, -1);
  const index = withoutTrailing.lastIndexOf("/");
  const parent = index >= 0 ? `${withoutTrailing.slice(0, index + 1)}` : "";
  return parent.length < rootPrefix.length ? rootPrefix : parent;
}

export function displayPath(bucket: BucketProfile, prefix: string): string {
  return `oss://${bucket.Name}/${normalizePrefix(prefix)}`;
}

export function relativeFolderName(prefix: string, rootPrefix: string): string {
  const value = prefix.startsWith(rootPrefix) ? prefix.slice(rootPrefix.length) : prefix;
  return value.replace(/\/+$/, "") || bucketPathLabel(rootPrefix);
}

function bucketPathLabel(prefix: string): string {
  const segments = prefix.replace(/\/+$/, "").split("/").filter(Boolean);
  return segments.at(-1) ?? "";
}

export function formatModified(value: string | null): string {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";
  const pad = (part: number) => String(part).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function isTextFile(name: string): boolean {
  return /\.(txt|md|markdown|json|jsonc|xml|html?|css|scss|less|js|mjs|cjs|ts|tsx|jsx|vue|yaml|yml|toml|ini|conf|log|csv|tsv|sql|sh|bat|cmd|ps1|py|java|c|cpp|h|hpp|cs|go|rs|php|rb|svg)$/i.test(
    name,
  );
}

export function isImageFile(name: string): boolean {
  return /\.(png|jpe?g|bmp|ico|gif|webp)$/i.test(name);
}

export function imageMime(name: string): string {
  const extension = name.split(".").pop()?.toLowerCase();
  return (
    {
      png: "image/png",
      jpg: "image/jpeg",
      jpeg: "image/jpeg",
      bmp: "image/bmp",
      ico: "image/x-icon",
      gif: "image/gif",
      webp: "image/webp",
    }[extension ?? ""] ?? "application/octet-stream"
  );
}

export function selectedEntries(
  items: ObjectEntry[],
  checked: Set<string>,
): ObjectEntry[] {
  return items.filter((item) => checked.has(item.Key));
}

export function objectFullPath(bucket: BucketProfile, entry: ObjectEntry): string {
  return `oss://${bucket.Name}/${entry.Key}`;
}

export function validateObjectName(value: string): string | null {
  const name = value.trim();
  if (!name) return "对象名称不能为空";
  if (/[\\/]/.test(name)) return "对象名称不能包含斜杠";
  if (name === "." || name === "..") return "对象名称不能是 . 或 ..";
  return null;
}

export function validateFolderName(value: string): string | null {
  const name = value.trim();
  if (!name) return "目录名称不能为空";
  if (/[\\/]/.test(name)) return "目录名称不能包含斜杠";
  if (name === "." || name === "..") return "目录名称不能是 . 或 ..";
  return null;
}

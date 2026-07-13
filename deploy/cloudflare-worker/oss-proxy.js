// ================================================================
// Cloudflare Worker — 阿里云 OSS 私有回源代理
// ================================================================
// 部署位置：download.seatflow.work/updates/*
// 密钥注入：wrangler secret put OSS_ACCESS_KEY_ID / OSS_ACCESS_KEY_SECRET
//          或通过 Cloudflare API PATCH secrets-bulk 下发
// ================================================================

export default {
    async fetch(request, env) {
        const url = new URL(request.url);
        const path = url.pathname;

        // 仅代理 /updates/ 路径
        if (!path.startsWith('/updates/')) {
            return fetch(request);
        }

        const accessKeyId = env.OSS_ACCESS_KEY_ID;
        const accessKeySecret = env.OSS_ACCESS_KEY_SECRET;
        if (!accessKeyId || !accessKeySecret) {
            return new Response('Missing OSS credentials', { status: 500 });
        }

        const bucket = 'seatflow-download';
        const region = 'oss-cn-hongkong';
        const endpoint = 'oss-cn-hongkong.aliyuncs.com';

        // ---- 构造 V4 签名 ----
        const now = new Date();
        const amzDate = now.toISOString().replace(/[:-]|\.\d{3}/g, '');
        const dateStamp = amzDate.slice(0, 8);

        const headersToSign = {
            'host': `${bucket}.${endpoint}`,
            'x-oss-content-sha256': 'UNSIGNED-PAYLOAD',
            'x-oss-date': amzDate,
        };

        const canonicalUri = encodePath(path);
        const canonicalQuery = '';
        const signedHeaderKeys = Object.keys(headersToSign).sort();
        const canonicalHeaders = signedHeaderKeys
            .map(k => `${k}:${headersToSign[k]}`)
            .join('\n') + '\n';
        const signedHeaders = signedHeaderKeys.join(';');

        const canonicalRequest = [
            'GET',
            canonicalUri,
            canonicalQuery,
            canonicalHeaders,
            signedHeaders,
            'UNSIGNED-PAYLOAD',
        ].join('\n');

        const algorithm = 'OSS4-HMAC-SHA256';
        const credentialScope = `${dateStamp}/${region}/oss/aliyun_v4_request`;

        const stringToSign = [
            algorithm,
            amzDate,
            credentialScope,
            await sha256Hex(canonicalRequest),
        ].join('\n');

        const signingKey = await deriveSigningKey(accessKeySecret, dateStamp, region);
        const signature = await hmacHex(signingKey, stringToSign);

        const authorization =
            `${algorithm} ` +
            `Credential=${accessKeyId}/${credentialScope}, ` +
            `SignedHeaders=${signedHeaders}, ` +
            `Signature=${signature}`;

        // ---- 回源请求 ----
        const ossUrl = `https://${bucket}.${endpoint}${canonicalUri}`;
        const upstream = await fetch(ossUrl, {
            method: 'GET',
            headers: {
                'host': headersToSign['host'],
                'x-oss-date': amzDate,
                'x-oss-content-sha256': 'UNSIGNED-PAYLOAD',
                'authorization': authorization,
            },
            redirect: 'follow',
        });

        // ---- 响应 ----
        const response = new Response(upstream.body, upstream);
        response.headers.set('Cache-Control', 'public, max-age=3600');
        response.headers.set('X-Served-By', 'cf-oss-proxy');
        return response;
    },
};

// ================================================================
// V4 签名算法
// ================================================================

async function deriveSigningKey(secret, dateStamp, region) {
    const kDate = await hmac(`aliyun_v4${secret}`, dateStamp);
    const kRegion = await hmac(kDate, region);
    const kService = await hmac(kRegion, 'oss');
    return hmac(kService, 'aliyun_v4_request');
}

// ================================================================
// 密码学工具
// ================================================================

async function sha256Hex(data) {
    const buf = new TextEncoder().encode(data);
    const hash = await crypto.subtle.digest('SHA-256', buf);
    return [...new Uint8Array(hash)].map(b => b.toString(16).padStart(2, '0')).join('');
}

async function hmac(key, data) {
    const encoder = new TextEncoder();
    const keyBuf = typeof key === 'string' ? encoder.encode(key) : new Uint8Array(key);
    const dataBuf = encoder.encode(data);
    const cryptoKey = await crypto.subtle.importKey(
        'raw', keyBuf, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']
    );
    const sig = await crypto.subtle.sign('HMAC', cryptoKey, dataBuf);
    return new Uint8Array(sig);
}

async function hmacHex(key, data) {
    const sig = await hmac(key, data);
    return [...sig].map(b => b.toString(16).padStart(2, '0')).join('');
}

function encodePath(path) {
    return path.split('/').map(encodeURIComponent).join('/');
}

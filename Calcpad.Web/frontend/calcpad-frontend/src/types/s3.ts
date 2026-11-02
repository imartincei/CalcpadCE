export type S3Role = 'Viewer' | 'Contributor' | 'Admin';
export type S3BucketKind = 'Library' | 'Project';

export interface S3User {
    id: string;
    username: string;
    email?: string;
    role: S3Role;
}

export interface S3Session {
    connected: boolean;
    url: string | null;
    user: S3User | null;
}

export interface S3Bucket {
    name: string;
    kind: S3BucketKind;
    createdBy: string;
    createdAt: string;
    access: S3Role;
}

export interface S3ObjectInfo {
    key: string;
    storageKey: string;
    latest: boolean;
    baseId: string;
    id: string;
    version: string;
    size: number;
    sha256: string;
    title: string;
    author: string;
    updatedBy: string;
    updatedAt: string;
}

/** A relayed local-server response; `data` is the parsed JSON body, or null when it had none. */
export interface S3Result {
    status: number;
    data: unknown;
}

export interface S3UploadRequest {
    bucket: string;
    key: string;
    content: string;
    sourceFilePath?: string;
    force: boolean;
}

export interface S3UploadResponse {
    info: S3ObjectInfo;
    content: string;
}

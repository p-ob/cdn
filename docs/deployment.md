# Deployment

This project provides out-of-the-box support for containerization and Kubernetes.

## Docker

A `Dockerfile` is provided at the repository root. It uses a multi-stage approach, building the .NET 10 solution and producing a minimal, production-ready image.

To build locally:
```bash
docker build -t npm-cdn .
```

## Kubernetes (K8s)

The `k8s/` directory contains standard manifests to deploy the CDN into a Kubernetes cluster. 

The manifests outline the following resources:
- **Deployment:** Manages the desired state and replication of the CDN pods.
- **Service:** Exposes the deployment.
- **ConfigMap:** Application configuration settings.
- **PersistentVolumeClaim (PVC):** Provisions storage for the standard Volume Mount cache.
- **Ingress:** Routes external HTTP/HTTPS traffic into the service.

### Applying Manifests

```bash
kubectl apply -f k8s/manifests.yaml
```

### Storage Configuration

Depending on your environment, you can configure the required `IStorageProvider`:

1. **Persistent Volumes (Default):** The provided Kubernetes manifests use a PVC mounted at `/var/cache/npm` to store the extracted tarballs. Ensure your cluster has a default StorageClass capable of dynamic provisioning (e.g., `gp2` on EKS), or map the PVC to an existing PV.
2. **AWS S3:** If you wish to use the AWS S3 backend (`NpmCdn.Storage.Aws`), ensure the Pods are granted sufficient IAM permissions (e.g., via IRSA on EKS). Then, provide the S3 bucket configuration via the application settings inside the `ConfigMap`.

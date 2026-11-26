// Direct-light PBR baseline; scene-linear inputs, explicit SDR output.
cbuffer Preview : register(b0) {
    row_major float4x4 Transform;
    row_major float4x4 Rotation;
    float Exposure, Roughness, Metallic, DebugMode;
};
struct Input { float3 P:POSITION; float3 N:NORMAL; float4 Cd:COLOR; float2 UV:TEXCOORD; };
struct Varying { float4 P:SV_Position; float3 N:NORMAL; float4 Cd:COLOR; float2 UV:TEXCOORD; };
Varying VS(Input v) {
    Varying o; o.P=mul(float4(v.P,1),Transform);
    o.N=mul(v.N,(float3x3)Rotation);o.Cd=v.Cd;o.UV=v.UV;return o;
}
float3 LinearToSrgb(float3 c) {
    c=saturate(c);return lerp(12.92*c,1.055*pow(c,1.0/2.4)-.055,step(.0031308,c));
}
float4 PS(Varying i,bool front:SV_IsFrontFace):SV_Target {
    float3 n=normalize(i.N);if(!front)n=-n;
    float3 base=max(i.Cd.rgb,0);
    if(DebugMode==1)return float4(LinearToSrgb(base),1);
    if(DebugMode==2)return float4(n*.5+.5,1);
    if(DebugMode==3){float tile=fmod(abs(floor(i.UV.x*12)+floor(i.UV.y*12)),2);return float4(lerp(.12,.8,tile).xxx,1);}
    float3 v=float3(0,0,1),l=normalize(float3(-.45,.7,1)),h=normalize(v+l);
    float nl=saturate(dot(n,l)),nv=max(saturate(dot(n,v)),.001),nh=saturate(dot(n,h)),vh=saturate(dot(v,h));
    float a=max(.045,Roughness);a*=a;float a2=a*a;
    float d=a2/(3.14159265*pow(nh*nh*(a2-1)+1,2));
    float k=pow(Roughness+1,2)/8;
    float g=nl/(nl*(1-k)+k)*nv/(nv*(1-k)+k);
    float3 f0=lerp(.04.xxx,base,Metallic);
    float3 f=f0+(1-f0)*pow(1-vh,5);
    float3 spec=d*g*f/max(4*nl*nv,.001);
    float3 diffuse=(1-f)*(1-Metallic)*base/3.14159265;
    float3 c=(diffuse+spec)*nl*3+base*.12;
    return float4(LinearToSrgb(1-exp(-c*exp2(Exposure))),1);
}

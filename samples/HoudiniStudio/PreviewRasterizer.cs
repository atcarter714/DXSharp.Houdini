using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DXSharp.Houdini.Studio;

internal readonly record struct SyncedView(Matrix4x4 ToCamera, Matrix4x4 ToPixels, int Width, int Height, bool Perspective);

internal sealed class PreviewRasterizer {
    private int _width, _height;
    private int[] _pixels = [];
    private float[] _depth = [];
    private Vector3[] _projected = [];
    private WriteableBitmap? _bitmap;
    private const int Background = unchecked((int)0xff101c29);
    private const int EdgeColor = unchecked((int)0xff69b6d5);

    public WriteableBitmap Render(int width, int height, Vector3[] points, (int A,int B)[] edges,
        int[] triangles, Vector3[] colors, Matrix4x4 rotation, double zoom, double panX, double panY, int mode, SyncedView? view=null) {
        width = Math.Clamp(width, 1, 1280);
        height = Math.Clamp(height, 1, 900);
        if (_bitmap is null || width != _width || height != _height) {
            _width = width; _height = height;
            _pixels = new int[width*height]; _depth = new float[width*height];
            _bitmap = new WriteableBitmap(width,height);
        }
        Array.Fill(_pixels, Background);
        Array.Fill(_depth, float.NegativeInfinity);
        DrawGrid();
        if (points.Length != 0) {
            if (_projected.Length != points.Length) _projected = new Vector3[points.Length];
            Vector3 lo = points[0], hi = lo;
            foreach (var p in points) { lo = Vector3.Min(lo,p); hi = Vector3.Max(hi,p); }
            Vector3 center = (lo+hi)*.5f;
            float scale = (float)(Math.Min(width,height)*.45*zoom/Math.Max((hi-lo).Length()*.5f,1e-6f));
            for (int i=0;i<points.Length;i++) {
                if(view is SyncedView v) {
                    var camera=Vector4.Transform(new Vector4(points[i],1),v.ToCamera);
                    var pixel=Vector4.Transform(camera,v.ToPixels);
                    if(!float.IsFinite(pixel.W)||Math.Abs(pixel.W)<1e-6f||(v.Perspective&&camera.Z>=-1e-5f)) {
                        _projected[i]=new(float.NaN); continue;
                    }
                    float fit=Math.Min(width/(float)Math.Max(1,v.Width),height/(float)Math.Max(1,v.Height));
                    _projected[i]=new((pixel.X/pixel.W-v.Width*.5f)*fit+width*.5f,
                        height*.5f-(pixel.Y/pixel.W-v.Height*.5f)*fit,v.Perspective?-1/camera.Z:camera.Z);
                } else {
                    var p=Vector3.Transform(points[i]-center,rotation)*scale;
                    _projected[i]=new(p.X+width*.5f+(float)panX,height*.5f-p.Y+(float)panY,p.Z);
                }
            }
            if (mode != 0) {
                for (int i=0;i+2<triangles.Length;i+=3) {
                    var a=_projected[triangles[i]]; var b=_projected[triangles[i+1]]; var c=_projected[triangles[i+2]];
                    if(!Finite(a)||!Finite(b)||!Finite(c)) continue;
                    var normal=Vector3.Cross(b-a,c-a);
                    if (normal.LengthSquared()<1e-12f) continue;
                    normal=Vector3.Normalize(normal);
                    float shade=.28f+.72f*Math.Abs(Vector3.Dot(normal,Vector3.Normalize(new Vector3(-.35f,-.45f,1))));
                    Vector3 fallback=new(.52f,.72f,.79f);
                    bool hasColor=colors.Length==points.Length;
                    var ca=hasColor?colors[triangles[i]]:fallback;
                    var cb=hasColor?colors[triangles[i+1]]:fallback;
                    var cc=hasColor?colors[triangles[i+2]]:fallback;
                    Triangle(a,b,c,ca,cb,cc,shade);
                }
            }
            if (mode != 1 || triangles.Length==0)
                foreach (var (a,b) in edges) Line(_projected[a],_projected[b],mode==2 && triangles.Length>0,view.HasValue?0.0001f:2f);
            if (edges.Length==0 && triangles.Length==0)
                foreach (var p in _projected) {
                    if(!Finite(p))continue;
                    int x=(int)p.X,y=(int)p.Y;
                    if ((uint)x<(uint)width && (uint)y<(uint)height) _pixels[y*width+x]=EdgeColor;
                }
        }
        using var stream=_bitmap.PixelBuffer.AsStream();
        stream.Position=0;
        stream.Write(MemoryMarshal.AsBytes(_pixels.AsSpan()));
        _bitmap.Invalidate();
        return _bitmap;
    }

    private void DrawGrid() {
        const int grid=unchecked((int)0xff192a3a);
        for(int y=0;y<_height;y+=32)
            for(int x=0;x<_width;x++) _pixels[y*_width+x]=grid;
        for(int x=0;x<_width;x+=32)
            for(int y=0;y<_height;y++) _pixels[y*_width+x]=grid;
    }
    private static float Cross(Vector3 a,Vector3 b,float x,float y) =>
        (b.X-a.X)*(y-a.Y)-(b.Y-a.Y)*(x-a.X);

    private void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 ca,Vector3 cb,Vector3 cc,float shade) {
        float area=Cross(a,b,c.X,c.Y);
        if(Math.Abs(area)<1e-6f) return;
        int left=Math.Max(0,(int)MathF.Floor(Math.Min(a.X,Math.Min(b.X,c.X))));
        int right=Math.Min(_width-1,(int)MathF.Ceiling(Math.Max(a.X,Math.Max(b.X,c.X))));
        int top=Math.Max(0,(int)MathF.Floor(Math.Min(a.Y,Math.Min(b.Y,c.Y))));
        int bottom=Math.Min(_height-1,(int)MathF.Ceiling(Math.Max(a.Y,Math.Max(b.Y,c.Y))));
        for(int y=top;y<=bottom;y++) for(int x=left;x<=right;x++) {
            float u=Cross(b,c,x+.5f,y+.5f)/area,v=Cross(c,a,x+.5f,y+.5f)/area,w=1-u-v;
            if(u<0 || v<0 || w<0) continue;
            float z=u*a.Z+v*b.Z+w*c.Z;
            int index=y*_width+x;
            if(z>_depth[index]) {
                _depth[index]=z;
                var rgb=Vector3.Clamp((ca*u+cb*v+cc*w)*shade,Vector3.Zero,Vector3.One);
                _pixels[index]=unchecked((int)0xff000000)|((int)(rgb.X*255)<<16)|((int)(rgb.Y*255)<<8)|(int)(rgb.Z*255);
            }
        }
    }
    private static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    private void Line(Vector3 a,Vector3 b,bool depthTest,float viewDepthTolerance) {
        if(!Finite(a)||!Finite(b))return;
        // Clip to viewport before stepping to bound work even when highly zoomed.
        float dx=b.X-a.X,dy=b.Y-a.Y,low=0,high=1;
        if(!Clip(-dx,a.X,ref low,ref high)||!Clip(dx,_width-1-a.X,ref low,ref high)||
           !Clip(-dy,a.Y,ref low,ref high)||!Clip(dy,_height-1-a.Y,ref low,ref high)) return;
        int count=Math.Max(1,(int)MathF.Ceiling(Math.Max(Math.Abs(dx),Math.Abs(dy))*(high-low)));
        for(int i=0;i<=count;i++) {
            var p=Vector3.Lerp(a,b,low+(high-low)*i/count);
            int x=(int)MathF.Round(p.X),y=(int)MathF.Round(p.Y);
            if((uint)x>=(uint)_width||(uint)y>=(uint)_height) continue;
            int k=y*_width+x;
            if(!depthTest || p.Z+(viewDepthTolerance)>=_depth[k]) _pixels[k]=EdgeColor;
        }
    }
    private static bool Clip(float p,float q,ref float low,ref float high) {
        if(p==0) return q>=0;
        float t=q/p;
        if(p<0) low=Math.Max(low,t); else high=Math.Min(high,t);
        return low<=high;
    }
}

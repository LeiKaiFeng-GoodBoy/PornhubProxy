启发项目
https://github.com/mashirozx/Pixiv-Nginx

提前说明一下,不同地区策略不同, 不保证一定能用,不保证一直可用,不支持登陆帐号,只能简单浏览
先测试一下是否可以访问这个网站https://www.livehub.com, 能上应该就可行,浏览器警告隐私错误是正常的

运行要求
.NETFramework v4.7.2以上


运行步骤
1.(最重要的)需要手动修改hosts文件,添加如下几项(WIN10可能需要刷新DNSClient服务才会生效,在cmd中运行引号中的命令"ipconfig /flushdns")

127.0.0.1 cn.pornhub.com

127.0.0.5 www.pornhub.com

127.0.0.1 hubt.pornhub.com



2.(可选)将文件夹中的myCA.crt导入到系统受信任的根证书列表,是受信任的根证书列表,是受信任的根证书列表,这一步是避免浏览器隐私警告,只有Chrome有效,Firefox不管用,所以不导入也可以用,只不过需要忽略浏览器警告

3.运行文件夹中的PornhubProxy.exe

4.在浏览器中访问链接(注意是HTTPS) https://cn.pornhub.com/


代码写的很糟糕,所以简单的说一下原理

我们知道一个服务器上,或者说一个ip地址上,可以托管不止一个网站,在HTTP协议中通过Host请求头来分别，现如今大多数网站都支TLS加密，建立加密链接后才会传输HTTP协议内容，但是建立TLS需要验证服务器证书，所以TLS握手协议添加了一个扩展字段用于客户端向服务器表明要验证哪个网站的证书
那么现在就有这样一种情况，TLS建立加密时会发送一个想要请求的网站的字符串，加密后HTTP协议仍可以发送Host请求头，只不过一般情况下这两个字符串的值都相同，如果不相同一般网站也会返回错误响应，但是理论上两个字段可以不相同，这要看目标网站的策略，假如允许，便会出现这样一种情况,假如一个ip上有两个网站A与B,我们建立加密时验证A网站的证书，也会明文发送一个A网站的域名字符串，当建立加密连接后我们将HTTP协议的Host请求头设置为B网站的域名，并且可以正常返回B网站的内容
我们需要考虑一下防火墙的策略，一般情况下防火墙并不仅仅只是根据ip地址来封锁，正如之前所说，一个ip地址上可能有好多网站，封掉一个ip可能会导致其他没问题网站也无法访问，所以防火墙会检查协议的内容，比如HTTP的Host请求头，TLS握手协议的明文部分，比如指示要验证那个网站证书的扩展字段，假设防火墙不允许访问B网站，但是允许与A网站建立加密链接，加密链接建立之后的明文内容理论上防火墙无法看到(但从整体上知道双方各传输了多少字节)所以我们可以在后续的HTTP协议中通过Host请求头请求B网站的内容
一个网站上的所有资源并不都在一个ip和一个域名当中，这也是这种办法的缺点，我们现在只是获取了网站的主体部分，还有图片资源与更重要的视频资源，但幸运的是网站中图片与视频资源的链接可以正常访问，只要获取了网站的主体html就可以正常加载其余部分
